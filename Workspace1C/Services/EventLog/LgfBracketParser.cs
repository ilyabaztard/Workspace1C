using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Workspace1C.Services.EventLog;

/// <summary>
/// Потоковый разборщик «скобочного» формата 1С (файлы журнала регистрации
/// <c>1Cv8.lgf</c> и <c>*.lgp</c>, выпуск 1.0.0.69). Узел — это либо атом
/// (<see cref="string"/>: число, буква, строка в кавычках без кавычек), либо
/// вложенный список (<see cref="List{T}"/> из <see cref="object"/>).
/// Строки в кавычках 1С экранирует удвоением кавычки (<c>""</c>).
/// Чтение идёт посимвольно из <see cref="TextReader"/>, поэтому большие
/// фрагменты журнала не материализуются в одну строку целиком.
/// </summary>
public sealed class LgfBracketParser
{
    private readonly TextReader _reader;

    public LgfBracketParser(TextReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    /// <summary>
    /// Читает следующую запись верхнего уровня (<c>{...}</c>), пропуская всё,
    /// что стоит между записями (запятые, переводы строк, заголовок файла).
    /// Возвращает null в конце потока или если запись оборвана (файл дописывается).
    /// </summary>
    public List<object>? ReadNextRecord()
    {
        while (true)
        {
            var c = _reader.Read();
            if (c < 0)
                return null;
            if (c == '{')
                return ReadListBody();
            if (c == '"')
                SkipQuoted();
        }
    }

    /// <summary>Разбирает тело списка после открывающей скобки.</summary>
    private List<object>? ReadListBody()
    {
        var list = new List<object>();
        var atom = new StringBuilder();
        var hasAtom = false;

        while (true)
        {
            var c = _reader.Read();
            if (c < 0)
                return null; // оборванная запись — журнал ещё дописывается

            switch (c)
            {
                case '{':
                {
                    var child = ReadListBody();
                    if (child is null)
                        return null;
                    list.Add(child);
                    hasAtom = false;
                    atom.Clear();
                    if (!SkipToSeparator(list, out var closed))
                        return null; // запись оборвана — журнал ещё дописывается
                    if (closed)
                        return list;
                    break;
                }
                case '"':
                {
                    var text = ReadQuoted();
                    if (text is null)
                        return null;
                    list.Add(text);
                    hasAtom = false;
                    atom.Clear();
                    if (!SkipToSeparator(list, out var closed))
                        return null; // запись оборвана — журнал ещё дописывается
                    if (closed)
                        return list;
                    break;
                }
                case ',':
                    list.Add(atom.ToString().Trim());
                    atom.Clear();
                    hasAtom = false;
                    break;
                case '}':
                    if (hasAtom || list.Count > 0)
                        list.Add(atom.ToString().Trim());
                    return list;
                default:
                    if (!char.IsWhiteSpace((char)c) || hasAtom)
                    {
                        atom.Append((char)c);
                        hasAtom = true;
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// После вложенного списка или строки в кавычках пропускает пробелы до запятой
    /// (следующий элемент) или закрывающей скобки (конец списка).
    /// Возвращает false, если поток закончился до разделителя (запись оборвана),
    /// true — если встречен разделитель; <paramref name="closed"/> при этом
    /// показывает, была ли это закрывающая скобка.
    /// </summary>
    private bool SkipToSeparator(List<object> list, out bool closed)
    {
        closed = false;
        while (true)
        {
            var c = _reader.Read();
            if (c < 0)
                return false; // оборванная запись
            if (c == ',')
                return true;
            if (c == '}')
            {
                closed = true;
                return true;
            }
        }
    }

    /// <summary>Читает строку в кавычках (открывающая уже прочитана); <c>""</c> → <c>"</c>.</summary>
    private string? ReadQuoted()
    {
        var sb = new StringBuilder();
        while (true)
        {
            var c = _reader.Read();
            if (c < 0)
                return null;
            if (c == '"')
            {
                if (_reader.Peek() == '"')
                {
                    _reader.Read();
                    sb.Append('"');
                    continue;
                }
                return sb.ToString();
            }
            sb.Append((char)c);
        }
    }

    private void SkipQuoted() => ReadQuoted();

    /// <summary>Разбирает одну запись из строки (для тестов и коротких фрагментов).</summary>
    public static List<object>? ParseSingle(string text)
    {
        using var reader = new StringReader(text ?? string.Empty);
        return new LgfBracketParser(reader).ReadNextRecord();
    }

    /// <summary>Атом узла по индексу или пустая строка.</summary>
    public static string AtomAt(List<object> node, int index) =>
        index >= 0 && index < node.Count && node[index] is string s ? s : string.Empty;

    /// <summary>Вложенный список узла по индексу или null.</summary>
    public static List<object>? ListAt(List<object> node, int index) =>
        index >= 0 && index < node.Count ? node[index] as List<object> : null;

    /// <summary>Целое значение атома или null.</summary>
    public static long? LongAt(List<object> node, int index) =>
        long.TryParse(AtomAt(node, index), System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;
}
