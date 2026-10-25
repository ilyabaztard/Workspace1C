using System;
using System.Collections.Generic;
using System.IO;

namespace Workspace1C.Services;

/// <summary>
/// Разбор директив <c>Include</c>/<c>IncludeOptional</c> из текста конфигурации Apache
/// (issue #63). Чистая функция без I/O — тестируется на обеих платформах.
/// </summary>
public static class ApacheConfIncludeParser
{
    /// <summary>
    /// Возвращает цели директив <c>Include path</c> и <c>IncludeOptional path</c>.
    /// Относительные пути резолвятся против каталога конфигурации
    /// <paramref name="confDirectory"/>; цели с wildcard «*» сохраняются как есть
    /// (обозначают каталог/маску). Комментарии (#) и прочие директивы игнорируются.
    /// </summary>
    public static IReadOnlyList<string> ParseIncludeTargets(string confText, string confDirectory)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(confText))
            return result;

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

            var rest = TrimQuotes(line.Substring(separator).Trim());
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
    public static string? ParseDocumentRoot(string? confText, string? resolveBase)
    {
        if (string.IsNullOrWhiteSpace(confText))
            return null;

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
            var target = TrimQuotes(line.Substring(separator).Trim());
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
    public static IReadOnlyList<int> ParseListenPorts(string? confText)
    {
        var result = new List<int>();
        if (string.IsNullOrWhiteSpace(confText))
            return result;

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
            var value = TrimQuotes(line.Substring(separator).Trim());
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