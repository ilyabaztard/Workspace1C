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