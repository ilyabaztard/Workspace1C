using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Workspace1C.Models;

namespace Workspace1C.Services.EventLog;

/// <summary>
/// Ридер последовательного формата журнала регистрации (выпуск 1.0.0.69, этап 2
/// функции «Журнал регистрации»): словарь <c>1Cv8.lgf</c> (пользователи, компьютеры,
/// приложения, события, метаданные) и фрагменты <c>*.lgp</c> с записями событий.
/// Формат используется платформой 8.2 и 8.3 по умолчанию (SQLite — по настройке).
/// Файлы открываются только на чтение с разрешением записи другим процессам:
/// работающая база продолжает дописывать журнал.
/// </summary>
public sealed class SequentialLgfReader : ILgdReader
{
    /// <summary>Формат имени фрагмента: дата начала фрагмента.</summary>
    public const string FragmentNameFormat = "yyyyMMddHHmmss";

    public LgdFormat Format => LgdFormat.Sequential;

    public bool CanRead(JournalLocation location) =>
        location is not null
        && location.Format == LgdFormat.Sequential
        && location.DataFiles.Count > 0;

    public long CountRows(JournalLocation location, CancellationToken ct = default)
    {
        EnsureCanRead(location);
        long count = 0;
        foreach (var file in location.DataFiles)
        {
            foreach (var _ in ReadRawRecords(file, ct))
                count++;
        }
        return count;
    }

    public IEnumerable<EventLogEntry> ReadPage(
        JournalLocation location,
        int skip,
        int take,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        EnsureCanRead(location);
        if (skip < 0)
            throw new ArgumentOutOfRangeException(nameof(skip));
        if (take <= 0)
            yield break;

        var dictionary = LgfDictionary.Load(location.MainFile);
        long rowId = 0;
        var emitted = 0;
        foreach (var file in location.DataFiles)
        {
            foreach (var record in ReadRawRecords(file, ct))
            {
                rowId++;
                if (rowId <= skip)
                    continue;
                var entry = MapRecord(record, dictionary, file, rowId);
                if (entry is null)
                    continue;
                yield return entry;
                emitted++;
                progress?.Report((double)emitted / take);
                if (emitted >= take)
                    yield break;
            }
        }
    }

    /// <summary>
    /// Читает записи фрагментов, пересекающихся с периодом [<paramref name="from"/>;
    /// <paramref name="to"/>], от новых фрагментов к старым. Внутри фрагмента
    /// записи отдаются от последней к первой. Фильтр по периоду применяется здесь же.
    /// </summary>
    public IEnumerable<EventLogEntry> ReadNewestFirst(
        JournalLocation location, DateTime from, DateTime to, CancellationToken ct = default)
    {
        EnsureCanRead(location);
        var dictionary = LgfDictionary.Load(location.MainFile);
        var files = SelectFragments(location.DataFiles, from, to);
        long rowBase = 0;
        for (var i = files.Count - 1; i >= 0; i--)
        {
            ct.ThrowIfCancellationRequested();
            var file = files[i];
            var entries = new List<EventLogEntry>();
            long localRow = 0;
            foreach (var record in ReadRawRecords(file, ct))
            {
                localRow++;
                var entry = MapRecord(record, dictionary, file, rowBase + localRow);
                if (entry is null)
                    continue;
                if (entry.Timestamp < from || entry.Timestamp > to)
                    continue;
                entries.Add(entry);
            }
            rowBase += localRow;
            for (var j = entries.Count - 1; j >= 0; j--)
                yield return entries[j];
        }
    }

    /// <summary>
    /// Отбор фрагментов по периоду по имени файла (дата начала фрагмента): фрагмент
    /// нужен, если начался не позже конца периода и следующий фрагмент начался не
    /// раньше начала периода. Файлы с нестандартным именем всегда включаются.
    /// Результат — по возрастанию даты начала.
    /// </summary>
    public static List<string> SelectFragments(IEnumerable<string> files, DateTime from, DateTime to)
    {
        var ordered = files
            .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            .ToList();
        var starts = ordered.Select(TryGetFragmentStart).ToList();
        var result = new List<string>();
        for (var i = 0; i < ordered.Count; i++)
        {
            var start = starts[i];
            if (start is null)
            {
                result.Add(ordered[i]);
                continue;
            }

            DateTime? nextStart = null;
            for (var j = i + 1; j < ordered.Count; j++)
            {
                if (starts[j] is not null)
                {
                    nextStart = starts[j];
                    break;
                }
            }

            if (start.Value > to)
                continue;
            if (nextStart is not null && nextStart.Value < from)
                continue;
            result.Add(ordered[i]);
        }
        return result;
    }

    /// <summary>Дата начала фрагмента из имени файла <c>yyyyMMddHHmmss.lgp</c> или null.</summary>
    public static DateTime? TryGetFragmentStart(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path ?? string.Empty);
        return DateTime.TryParseExact(name, FragmentNameFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal, out var value)
            ? value
            : null;
    }

    /// <summary>Открывает файл журнала на чтение, не мешая 1С дописывать его.</summary>
    internal static StreamReader OpenShared(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 1 << 16);
        return new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, 1 << 16);
    }

    /// <summary>Все записи верхнего уровня файла (заголовок пропускается).</summary>
    public static IEnumerable<List<object>> ReadRawRecords(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path))
            yield break;

        using var reader = OpenShared(path);
        var parser = new LgfBracketParser(reader);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var record = parser.ReadNextRecord();
            if (record is null)
                yield break;
            yield return record;
        }
    }

    /// <summary>
    /// Преобразует запись фрагмента в <see cref="EventLogEntry"/>. Поля записи:
    /// 0 — дата (yyyyMMddHHmmss), 1 — статус транзакции (N/U/R/C), 2 — транзакция,
    /// 3 — пользователь, 4 — компьютер, 5 — приложение, 6 — соединение, 7 — событие,
    /// 8 — важность (I/W/E/N), 9 — комментарий, 10 — метаданные, 11 — данные,
    /// 12 — представление данных, 13 — сервер, 14/15 — порты, 16 — сеанс.
    /// Запись без корректной даты пропускается (null).
    /// </summary>
    public static EventLogEntry? MapRecord(List<object> record, LgfDictionary dictionary, string sourceFile, long rowId)
    {
        if (record.Count < 9)
            return null;

        var dateText = LgfBracketParser.AtomAt(record, 0);
        if (!DateTime.TryParseExact(dateText, FragmentNameFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out var timestamp))
            return null;

        var entry = new EventLogEntry
        {
            RowId = rowId,
            Timestamp = timestamp,
            TransactionStatus = MapTransactionStatus(LgfBracketParser.AtomAt(record, 1)),
            User = dictionary.Resolve(LgfDictionary.UserKind, LgfBracketParser.LongAt(record, 3)),
            Computer = dictionary.Resolve(LgfDictionary.ComputerKind, LgfBracketParser.LongAt(record, 4)),
            Application = dictionary.Resolve(LgfDictionary.ApplicationKind, LgfBracketParser.LongAt(record, 5)),
            Severity = MapSeverity(LgfBracketParser.AtomAt(record, 8)),
            Comment = LgfBracketParser.AtomAt(record, 9),
            SourceFile = sourceFile,
        };

        var eventCode = LgfBracketParser.LongAt(record, 7);
        entry.EventCode = (int)(eventCode ?? 0);
        entry.EventName = dictionary.Resolve(LgfDictionary.EventKind, eventCode);

        var metadataCode = LgfBracketParser.LongAt(record, 10);
        if (metadataCode is not null && metadataCode.Value != 0)
        {
            var metadataName = dictionary.Resolve(LgfDictionary.MetadataKind, metadataCode);
            entry.MetadataName = metadataName;
            var dot = metadataName.IndexOf('.');
            entry.MetadataType = dot > 0 ? metadataName.Substring(0, dot) : string.Empty;
        }

        entry.Data = DescribeData(LgfBracketParser.ListAt(record, 11));
        entry.DataPresentation = LgfBracketParser.AtomAt(record, 12);
        entry.Session = LgfBracketParser.LongAt(record, 16) ?? 0;

        return entry;
    }

    /// <summary>Краткое представление поля «Данные», если 1С не записала представление.</summary>
    public static string DescribeData(List<object>? data)
    {
        if (data is null || data.Count == 0)
            return string.Empty;
        var kind = data[0] as string ?? string.Empty;
        if (kind == "U")
            return string.Empty;
        if (data.Count >= 2 && data[1] is string value)
            return value;
        return string.Empty;
    }

    public static EventLogSeverity MapSeverity(string code) => code switch
    {
        "W" => EventLogSeverity.Warning,
        "E" => EventLogSeverity.Error,
        "N" => EventLogSeverity.Note,
        _ => EventLogSeverity.Info,
    };

    public static EventLogTransactionStatus MapTransactionStatus(string code) => code switch
    {
        "U" => EventLogTransactionStatus.Committed,
        "R" => EventLogTransactionStatus.RolledBack,
        "C" => EventLogTransactionStatus.Started,
        _ => EventLogTransactionStatus.None,
    };

    private void EnsureCanRead(JournalLocation location)
    {
        if (!CanRead(location))
            throw new InvalidOperationException(
                $"Ридер {nameof(SequentialLgfReader)} не может прочитать расположение журнала " +
                $"(формат: {location?.Format ?? LgdFormat.Unknown}).");
    }
}

/// <summary>
/// Словарь журнала <c>1Cv8.lgf</c>: коды → имена. Виды записей словаря:
/// 1 — пользователь <c>{1,uuid,"Имя",код}</c>, 2 — компьютер <c>{2,"Имя",код}</c>,
/// 3 — приложение, 4 — событие, 5 — метаданные <c>{5,uuid,"Имя",код}</c>,
/// 6 — сервер, 7/8 — порты. Код — всегда последний элемент записи,
/// имя — предпоследний.
/// </summary>
public sealed class LgfDictionary
{
    public const int UserKind = 1;
    public const int ComputerKind = 2;
    public const int ApplicationKind = 3;
    public const int EventKind = 4;
    public const int MetadataKind = 5;

    private readonly Dictionary<(int Kind, long Code), string> _names = new();

    /// <summary>Число загруженных элементов словаря.</summary>
    public int Count => _names.Count;

    /// <summary>Добавляет запись словаря (повторный код перезаписывает имя).</summary>
    public void Add(List<object> record)
    {
        if (record.Count < 3)
            return;
        var kind = LgfBracketParser.LongAt(record, 0);
        var code = LgfBracketParser.LongAt(record, record.Count - 1);
        if (kind is null || code is null)
            return;
        var name = LgfBracketParser.AtomAt(record, record.Count - 2);
        _names[((int)kind.Value, code.Value)] = name;
    }

    /// <summary>Имя по виду и коду; неизвестный код — «#код», отсутствующий — пусто.</summary>
    public string Resolve(int kind, long? code)
    {
        if (code is null)
            return string.Empty;
        return _names.TryGetValue((kind, code.Value), out var name) ? name : $"#{code.Value}";
    }

    /// <summary>Загружает словарь из файла <c>1Cv8.lgf</c>; отсутствующий файл — пустой словарь.</summary>
    public static LgfDictionary Load(string? path)
    {
        var dictionary = new LgfDictionary();
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return dictionary;
        foreach (var record in SequentialLgfReader.ReadRawRecords(path))
            dictionary.Add(record);
        return dictionary;
    }
}
