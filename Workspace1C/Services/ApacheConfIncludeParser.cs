using System;
using System.Collections.Generic;
using System.IO;

namespace Workspace1C.Services;

/// <summary>
/// Разбор директив <c>Include</c>/<c>IncludeOptional</c>, <c>Define</c>, <c>DocumentRoot</c>
/// и <c>Listen</c> из текста конфигурации Apache (issue #63). Чистая функция без I/O —
/// тестируется на обеих платформах.
/// <para>
/// С версии 1.0.0.73 поддерживаются директивы <c>Define</c> и подстановка переменных
/// <c>${VAR}</c> в значениях директив (типовые конфиги задают пути через
/// <c>Define SRVROOT "c:/Apache/Apache24"</c>, а порт — через <c>Define OneCPort 8027</c>):
/// <see cref="ParseDefines"/> собирает словарь, <see cref="ExpandVariables"/> раскрывает
/// его в значении <c>DocumentRoot</c>/<c>Listen</c>/<c>Include</c> — иначе каталог
/// публикации и порт оставались бы с литералами <c>${…}</c> (замечание @7OH в issue #63).
/// </para>
/// </summary>
public static class ApacheConfIncludeParser
{
    /// <summary>
    /// Возвращает цели директив <c>Include path</c> и <c>IncludeOptional path</c>.
    /// Относительные пути резолвятся против каталога конфигурации
    /// <paramref name="confDirectory"/>; цели с wildcard «*» сохраняются как есть
    /// (обозначают каталог/маску). Комментарии (#) и прочие директивы игнорируются.
    /// Значения раскрываются по переменным <c>Define</c> (issue #63); готовый словарь
    /// можно передать в <paramref name="defines"/> (иначе вычисляется из текста).
    /// </summary>
    public static IReadOnlyList<string> ParseIncludeTargets(string confText, string confDirectory,
        IReadOnlyDictionary<string, string>? defines = null)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(confText))
            return result;

        var vars = defines ?? ParseDefines(confText);
        confDirectory = string.IsNullOrWhiteSpace(confDirectory) ? "." : confDirectory;
        var lines = confText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;

            var separator = line.IndexOfAny(new[] { ' ', '\t' });
            if (separator <= 0)
                continue;

            var directive = line.Substring(0, separator);
            if (!IsInclude(directive))
                continue;

            var rest = ExpandVariables(TrimQuotes(line.Substring(separator).Trim()), vars);
            if (string.IsNullOrWhiteSpace(rest))
                continue;

            result.Add(Resolve(rest, confDirectory));
        }

        return result;
    }

    /// <summary>Директива Include/IncludeOptional (в Apache регистр не важен).</summary>
    public static bool IsInclude(string directive)
        => string.Equals(directive, "Include", StringComparison.OrdinalIgnoreCase)
           || string.Equals(directive, "IncludeOptional", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Директивы <c>Define NAME value</c> / <c>Define NAME "value"</c> из текста конфига
    /// (issue #63) как словарь (регистронезависимый по имени). Позднее определение
    /// перезаписывает более раннее; значение снимается с кавычек. Регистр директивы
    /// не важен; строки без значения и прочие директивы игнорируются.
    /// <para>Apache разрешает варианты имени в <c>${Name}</c>, <c>${NAME}</c> и т.п. —
    /// словарь сравнивает ключи без учёта регистра, раскрытие работает для любого вида.</para>
    /// </summary>
    public static IReadOnlyDictionary<string, string> ParseDefines(string? confText)
    {
        var defines = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(confText))
            return defines;

        foreach (var raw in SplitLines(confText))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;
            var separator = line.IndexOfAny(new[] { ' ', '\t' });
            if (separator <= 0)
                continue;
            var directive = line.Substring(0, separator);
            if (!string.Equals(directive, "Define", StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = TrimQuotes(line.Substring(separator).Trim());
            if (string.IsNullOrWhiteSpace(rest))
                continue;
            var nameSeparator = rest.IndexOfAny(new[] { ' ', '\t' });
            var name = nameSeparator > 0 ? rest[..nameSeparator] : rest;
            if (name.Length == 0)
                continue;
            var value = nameSeparator > 0 ? TrimQuotes(rest[(nameSeparator + 1)..].Trim()) : string.Empty;
            defines[name] = value;
        }

        return defines;
    }

    /// <summary>
    /// Объединяет словари <c>Define</c> нескольких текстов конфигов (главный + включаемые
    /// файлы): поздние переопределяют ранние — семантика Apache по порядку подключения
    /// <c>Include</c> (issue #63, «переменные могут быть объявлены до include»). Возвращает
    /// новый словарь; пустые тексты пропускаются. Раскрытие <c>${VAR}</c> затем выполняется
    /// одним глобальным словарём по ВСЕМ файлам (двухпроходный разбор).
    /// </summary>
    public static IReadOnlyDictionary<string, string> MergeDefines(IReadOnlyCollection<string> confTexts)
    {
        var defines = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (confTexts is null)
            return defines;

        foreach (var confText in confTexts)
        {
            if (string.IsNullOrWhiteSpace(confText))
                continue;
            foreach (var pair in ParseDefines(confText))
                defines[pair.Key] = pair.Value;
        }

        return defines;
    }

    /// <summary>
    /// Раскрывает переменные <c>${Name}</c> в тексте по словарю <see cref="ParseDefines"/>
    /// (issue #63). Имя переменной ищется регистронезависимо. Неизвестная переменная
    /// остаётся в строке как есть (не роняем разбор); без словаря/переменных текст
    /// возвращается без изменений.
    /// </summary>
    public static string ExpandVariables(string? text, IReadOnlyDictionary<string, string>? defines)
    {
        if (string.IsNullOrEmpty(text) || defines is null || defines.Count == 0 || text!.IndexOf("${", StringComparison.Ordinal) < 0)
            return text ?? string.Empty;

        var result = new System.Text.StringBuilder(text.Length + 32);
        for (var i = 0; i < text!.Length; i++)
        {
            if (text[i] == '$' && i + 1 < text.Length && text[i + 1] == '{')
            {
                var closing = text.IndexOf('}', i + 2);
                if (closing > i + 2)
                {
                    var name = text.Substring(i + 2, closing - i - 2);
                    if (defines.TryGetValue(name, out var value))
                    {
                        result.Append(value ?? string.Empty);
                        i = closing;
                        continue;
                    }
                }
            }
            result.Append(text[i]);
        }
        return result.ToString();
    }

    /// <summary>
    /// Объединяет базовый каталог и относительный путь. POSIX-каталог (начинается
    /// с «/») собирается без участия Windows-семантики Path.Combine (иначе на
    /// Windows из «/etc/apache2» получался бы «g:\etc\apache2»).
    /// </summary>
    internal static string CombinePath(string basePath, string relative)
    {
        if (string.IsNullOrWhiteSpace(basePath))
            return relative;
        if (basePath.StartsWith("/", StringComparison.Ordinal))
            return basePath.TrimEnd('/') + "/" + relative.TrimStart('/');
        return Path.GetFullPath(Path.Combine(basePath, relative));
    }

    /// <summary>
    /// Первая валидная директива <c>DocumentRoot</c> из текста конфига (issue #63):
    /// каталог публикации по умолчанию. Относительный путь резолвится против
    /// <paramref name="resolveBase"/> (обычно ServerRoot или каталог конфига);
    /// <c>null</c> — директива не найдена.
    /// </summary>
    public static string? ParseDocumentRoot(string? confText, string? resolveBase,
        IReadOnlyDictionary<string, string>? defines = null)
    {
        if (string.IsNullOrWhiteSpace(confText))
            return null;

        var vars = defines ?? ParseDefines(confText);
        foreach (var raw in SplitLines(confText))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;
            var separator = line.IndexOfAny(new[] { ' ', '\t' });
            if (separator <= 0)
                continue;
            var directive = line.Substring(0, separator);
            if (!string.Equals(directive, "DocumentRoot", StringComparison.OrdinalIgnoreCase))
                continue;
            var target = ExpandVariables(TrimQuotes(line.Substring(separator).Trim()), vars);
            if (string.IsNullOrWhiteSpace(target))
                continue;
            return string.IsNullOrWhiteSpace(resolveBase) ? target : Resolve(target, resolveBase);
        }

        return null;
    }

    /// <summary>
    /// Порты директив <c>Listen</c> из текста конфига (по порядку, без дублей; issue #63):
    /// «Listen 80», «Listen 0.0.0.0:80», «Listen [::]:8080». Пустой результат — конфиг без
    /// Listen (порт 80 по умолчанию).
    /// </summary>
    public static IReadOnlyList<int> ParseListenPorts(string? confText,
        IReadOnlyDictionary<string, string>? defines = null)
    {
        var result = new List<int>();
        if (string.IsNullOrWhiteSpace(confText))
            return result;

        var vars = defines ?? ParseDefines(confText);
        var seen = new HashSet<int>();
        foreach (var raw in SplitLines(confText))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;
            var separator = line.IndexOfAny(new[] { ' ', '\t' });
            if (separator <= 0)
                continue;
            var directive = line.Substring(0, separator);
            if (!string.Equals(directive, "Listen", StringComparison.OrdinalIgnoreCase))
                continue;
            var value = ExpandVariables(TrimQuotes(line.Substring(separator).Trim()), vars);
            if (string.IsNullOrEmpty(value))
                continue;

            var portPart = value;
            var colon = value.LastIndexOf(':');
            if (colon >= 0)
                portPart = value[(colon + 1)..];
            if (!int.TryParse(portPart.Trim(), out var port) || port is < 1 or > 65535)
                continue;
            if (seen.Add(port))
                result.Add(port);
        }

        return result;
    }

    private static IEnumerable<string> SplitLines(string confText)
        => confText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

    private static string Resolve(string target, string confDirectory)
    {
        // POSIX-абсолютный путь (Linux): сохраняем слэши как есть и на Windows.
        if (target.StartsWith("/", StringComparison.Ordinal))
            return target;
        if (Path.IsPathRooted(target))
            return Path.GetFullPath(target);
        return CombinePath(confDirectory, target);
    }

    private static string TrimQuotes(string value)
    {
        value = value.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            return value.Substring(1, value.Length - 2).Trim();
        return value;
    }
}