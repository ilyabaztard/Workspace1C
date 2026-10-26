using System;
using System.Collections.Generic;
using System.IO;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Обнаружение установленных инстансов Apache (issue #63). Платформенная часть:
/// Windows — службы httpd (ImagePath, ключи <c>-d</c>/<c>-f</c>), Linux — процессы и вывод
/// <c>apache2ctl -V</c>/<c>httpd -V</c>/<c>-S</c>. Чистые функции разбора (<see cref="ParseWindowsServiceImagePath"/>,
/// <see cref="ParseApacheCtl"/>) не зависят от платформы и покрыты юнит-тестами.
/// Если инстансы не найдены, окно публикации использует прежние
/// <see cref="WebPublishService.SuggestApacheConf"/>/<see cref="WebPublishService.SuggestPhysicalDirectory"/>.
/// </summary>
public static partial class ApacheInstanceDetector
{
    /// <summary>
    /// Инстансы Apache, найденные на текущей платформе (пустой список — не найдены).
    /// Ошибки платформенного опроса не роняют приложение.
    /// </summary>
    public static IReadOnlyList<ApacheInstance> Detect()
    {
#if WINDOWS
        return DetectWindows();
#else
        return DetectLinux();
#endif
    }

    /// <summary>
    /// Похож ли инстанс на Apache по имени/описанию (httpd / apache).
    /// Используется при фильтрации служб и процессов.
    /// </summary>
    public static bool LooksLikeApache(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var value = text.Trim();
        return value.IndexOf("httpd", StringComparison.OrdinalIgnoreCase) >= 0
               || value.IndexOf("apache", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Windows: инстанс Apache из командной строки службы httpd
    /// (например <c>"C:\Apache24\bin\httpd.exe" -d "C:\Apache24" -f "C:\Apache24\conf\httpd.conf"</c>).
    /// Конфиг: ключ <c>-f</c> или по умолчанию <c>ServerRoot\conf\httpd.conf</c>; htdocs —
    /// <c>ServerRoot\htdocs</c>. Возвращает null, если в командной строке нет ни корня, ни конфига.
    /// </summary>
    public static ApacheInstance? ParseWindowsServiceImagePath(
        string serviceName, string displayName, string description, string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            return null;

        var serverRoot = ExtractSwitch(imagePath, "d");
        var confPath = ExtractSwitch(imagePath, "f");

        // ServerRoot нужен и для резолва относительного конфига (-f conf\httpd27.conf),
        // поэтому выводим его из каталога исполняемого файла и при отсутствии ключа -d.
        if (string.IsNullOrWhiteSpace(serverRoot))
            serverRoot = InferServerRoot(imagePath);

        if (string.IsNullOrWhiteSpace(serverRoot) && string.IsNullOrWhiteSpace(confPath))
            return null;

        serverRoot = NormalizePath(serverRoot);
        confPath = NormalizePath(confPath);

        // Относительный путь конфига (-f conf\httpd27.conf) Apache трактует от ServerRoot —
        // иначе File.Exists(ConfPath) ищет файл от каталога процесса и инстанс остаётся
        // без DocumentRoot/портов (issue #63, «Пока не удалось»).
        if (!string.IsNullOrWhiteSpace(confPath) && !string.IsNullOrWhiteSpace(serverRoot) && !IsRootedPath(confPath))
            confPath = Path.Combine(serverRoot, confPath);
        else if (string.IsNullOrWhiteSpace(confPath) && !string.IsNullOrWhiteSpace(serverRoot))
            confPath = Path.Combine(serverRoot, "conf", "httpd.conf");
        confPath = NormalizePath(confPath);

        var htdocs = string.IsNullOrWhiteSpace(serverRoot)
            ? string.Empty
            : Path.Combine(serverRoot, "htdocs");

        return new ApacheInstance
        {
            Id = serviceName,
            Name = serviceName,
            Description = FirstNonEmpty(displayName, description),
            ServerRoot = serverRoot ?? string.Empty,
            ConfPath = confPath ?? string.Empty,
            Htdocs = htdocs,
            Kind = LooksLikeApache22(serverRoot) ? WebServerKind.Apache22 : WebServerKind.Apache24
        };
    }

    /// <summary>
    /// Linux: инстанс Apache из вывода <c>apache2ctl -V</c>/<c>httpd -V</c> и
    /// <c>apache2ctl -S</c>/<c>httpd -S</c>. Directory-директивы вида
    /// <c>-D HTTPD_ROOT="/etc/apache2"</c>, <c>-D SERVER_CONFIG_FILE="conf/httpd.conf"</c>.
    /// Относительный конфиг резолвится против HTTPD_ROOT. Конфиг по умолчанию —
    /// <paramref name="defaultConfPath"/>, htdocs — <paramref name="defaultHtdocs"/>.
    /// </summary>
    public static ApacheInstance? ParseApacheCtl(
        string? apacheCtlOutput, string? apacheCtlSOuput, string defaultConfPath, string defaultHtdocs)
    {
        if (string.IsNullOrWhiteSpace(apacheCtlOutput))
            return null;

        var serverRoot = ExtractDirective(apacheCtlOutput, "HTTPD_ROOT");
        var confPath = ExtractDirective(apacheCtlOutput, "SERVER_CONFIG_FILE");
        if (!string.IsNullOrWhiteSpace(confPath)
            && !Path.IsPathRooted(confPath)
            && !confPath.StartsWith("/", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(serverRoot))
        {
            confPath = ApacheConfIncludeParser.CombinePath(serverRoot!, confPath);
        }

        if (string.IsNullOrWhiteSpace(confPath))
            confPath = defaultConfPath;
        if (string.IsNullOrWhiteSpace(serverRoot) && !string.IsNullOrWhiteSpace(confPath))
            serverRoot = Path.GetDirectoryName(confPath) ?? string.Empty;

        var htdocs = string.IsNullOrWhiteSpace(apacheCtlSOuput)
            ? defaultHtdocs
            : ExtractDocumentRoot(apacheCtlSOuput, defaultHtdocs);

        var version = ExtractServerVersion(apacheCtlOutput);
        var major = ParseMajorVersion(version);

        return new ApacheInstance
        {
            Id = $"{FirstNonEmpty(version, "Apache")} ({FirstNonEmpty(serverRoot, "-")})",
            Name = FirstNonEmpty(version, "Apache"),
            Description = version ?? string.Empty,
            ServerRoot = serverRoot ?? string.Empty,
            ConfPath = confPath ?? string.Empty,
            Htdocs = htdocs ?? defaultHtdocs,
            Kind = IsApache22(version, major) ? WebServerKind.Apache22 : WebServerKind.Apache24
        };
    }

    /// <summary>
    /// Читает include-директивы конфига и заполняет <see cref="ApacheInstance.IncludeFiles"/>/
    /// <see cref="ApacheInstance.IncludeDirs"/> инстанса (разбор по <see cref="ApacheConfIncludeParser"/>),
    /// а также точный <see cref="ApacheInstance.DocumentRoot"/> и порты
    /// <see cref="ApacheInstance.ListenPorts"/>. Директивы <c>Define</c> учитываются при
    /// раскрытии <c>${VAR}</c> в значениях (issue #63: «а переменные из файла кто подставлять
    /// будет сейчас?» — иначе DocumentRoot/порт оставались бы с литералами).
    /// <para>
    /// Include-граф раскрывается ИТЕРАТИВНО по всему дереву (не только 1-й уровень):
    /// на каждом проходе все уже известные файлы заново раскрываются по ОБЩЕМУ словарю
    /// <c>Define</c> (<see cref="ApacheConfIncludeParser.MergeDefines"/>), пока не перестанут
    /// появляться новые файлы — Define из любого файла графа видны при раскрытии
    /// <c>Include</c>/<c>DocumentRoot</c>/<c>Listen</c> независимо от порядка объявления
    /// (issue #63, комментарий 7OH: «Переменные могут быть объявлены до include»,
    /// главный файл запуска — не обязательно httpd.conf, а <c>-f conf\httpd27.conf</c>).
    /// Относительные <c>Include</c>/<c>DocumentRoot</c> резолвятся от ServerRoot (семантика
    /// Apache), fallback — каталог конфига. <paramref name="readFileText"/> — чтение текста
    /// файла (в тестах — словарь).
    /// </para>
    /// </summary>
    public static void PopulateIncludes(ApacheInstance instance, string? confText, string? confDirectory,
        Func<string, string?>? readFileText = null)
    {
        if (instance is null)
            return;

        readFileText ??= File.ReadAllText;

        var text = confText ?? string.Empty;
        // Относительный Include/DocumentRoot Apache трактует от ServerRoot, а не от каталога
        // включающего файла: «Include conf/httpd.conf» из conf\httpd27.conf → ServerRoot\conf\httpd.conf.
        // Fallback (ServerRoot неизвестен) — каталог конфига.
        var baseRoot = !string.IsNullOrWhiteSpace(instance.ServerRoot)
            ? instance.ServerRoot
            : (confDirectory ?? string.Empty);

        var defines = ApacheConfIncludeParser.ParseDefines(text);
        var allTexts = new List<(string Path, string Text)> { (instance.ConfPath ?? string.Empty, text) };
        var visitedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            NormalizeVisitKey(instance.ConfPath)
        };
        var includeFiles = new List<string>();
        var includeDirs = new List<string>();
        var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Раскрытие графа до стабилизации: копия списка на проход (список пополняется
        // во время обхода, менять коллекцию в foreach нельзя).
        while (true)
        {
            var added = false;
            foreach (var (_, fileText) in allTexts.ToArray())
            {
                foreach (var target in ApacheConfIncludeParser.ParseIncludeTargets(fileText, baseRoot, defines))
                {
                    if (!seenTargets.Add(target))
                        continue;
                    if (target.Contains('*') || target.Contains('?'))
                    {
                        includeDirs.Add(target);
                        continue;
                    }
                    // Неразрешённая переменная (${...}) в Include-цели не попадает в список:
                    // файл по такому пути всё равно не прочитается, а на следующем проходе
                    // цель раскроется корректно (Define может быть в другом файле графа) —
                    // «сырое» имя с ${...} в окне публикации не показываем (issue #63).
                    if (target.IndexOf("${", StringComparison.Ordinal) >= 0)
                        continue;
                    includeFiles.Add(target);
                    if (!visitedPaths.Add(NormalizeVisitKey(target)))
                        continue;
                    try
                    {
                        var includeText = readFileText(target);
                        if (!string.IsNullOrWhiteSpace(includeText))
                        {
                            allTexts.Add((target, includeText));
                            added = true;
                        }
                    }
                    catch
                    {
                        // Файл отсутствует/недоступен — не роняем разбор.
                    }
                }
            }

            if (!added)
                break;

            // ОБЩИЙ Define по всем известным файлам графа (поздние переопределяют ранние).
            defines = ApacheConfIncludeParser.MergeDefines(allTexts.Select(t => t.Text).ToList());
        }

        instance.IncludeFiles = includeFiles;
        instance.IncludeDirs = includeDirs;

        // Точный DocumentRoot и порты Listen (issue #63): каталог публикации по умолчанию
        // (может включать подкаталог сайта — «ServerRoot\htdocs\27») и порт URL. DocumentRoot
        // и Listen могут находиться В ЛЮБОМ файле графа — ищем по всем текстам. Неразрешённую
        // переменную (${...}) как DocumentRoot не берём — в окне остаётся «понятный» каталог
        // (Htdocs/SuggestPhysicalDirectory), а не сырое значение (issue #63).
        if (string.IsNullOrWhiteSpace(instance.DocumentRoot))
        {
            foreach (var (_, fileText) in allTexts)
            {
                var root = ApacheConfIncludeParser.ParseDocumentRoot(fileText, baseRoot, defines);
                if (string.IsNullOrWhiteSpace(root) || root.IndexOf("${", StringComparison.Ordinal) >= 0)
                    continue;
                instance.DocumentRoot = root;
                break;
            }
            instance.DocumentRoot ??= string.Empty;
        }

        if (instance.ListenPorts is null || instance.ListenPorts.Count == 0)
        {
            var ports = new List<int>();
            var seen = new HashSet<int>();
            foreach (var (_, fileText) in allTexts)
            {
                foreach (var port in ApacheConfIncludeParser.ParseListenPorts(fileText, defines))
                {
                    if (seen.Add(port))
                        ports.Add(port);
                }
            }
            instance.ListenPorts = ports;
        }
    }

    private static string? ExtractSwitch(string commandLine, string key)
    {
        var tokens = Tokenize(commandLine);
        for (var i = 0; i + 1 < tokens.Count; i++)
        {
            if ((string.Equals(tokens[i], "-" + key, StringComparison.OrdinalIgnoreCase)
                 || string.Equals(tokens[i], "/" + key, StringComparison.OrdinalIgnoreCase))
                && !tokens[i + 1].StartsWith("-", StringComparison.Ordinal)
                && !tokens[i + 1].StartsWith("/", StringComparison.Ordinal)
                && tokens[i + 1].Length > 0)
            {
                return tokens[i + 1];
            }
        }

        // Слитные формы ключа «-f=conf\httpd27.conf» / «/f:"conf\httpd27.conf"» (некоторые
        // обёртки служб записывают ImagePath без пробела между ключом и значением) —
        // конфиг, указанный в службе, должен обязательно стать ConfPath инстанса (issue #63).
        for (var i = 0; i < tokens.Count; i++)
        {
            var value = ExtractInlineSwitch(tokens[i], key);
            if (value is not null)
                return value;
        }

        return null;
    }

    /// <summary>
    /// Значение слитного ключа вида «-f=value», «-f:value», «/f:value». Форма без
    /// разделителя («-fvalue») намеренно не распознаётся — иначе посторонние ключи
    /// вида «-debug» ошибочно приняли бы «ebug» за значение конфига.
    /// </summary>
    private static string? ExtractInlineSwitch(string token, string key)
    {
        foreach (var prefix in new[]
        {
            "-" + key + "=", "-" + key + ":",
            "/" + key + "=", "/" + key + ":"
        })
        {
            if (!token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;
            var value = token.Substring(prefix.Length).Trim();
            if (value.Length == 0)
                continue;
            value = value.Trim('"');
            return value.Length == 0 ? null : value;
        }
        return null;
    }

    /// <summary>ServerRoot по каталогу исполняемого файла (bin\..\.. для httpd.exe).</summary>
    private static string? InferServerRoot(string commandLine)
    {
        var tokens = Tokenize(commandLine);
        if (tokens.Count == 0)
            return null;
        var exe = tokens[0];
        var dir = Path.GetDirectoryName(exe);
        if (string.IsNullOrWhiteSpace(dir))
            return null;
        return string.Equals(Path.GetFileName(dir), "bin", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(Path.GetFullPath(dir))
            : Path.GetFullPath(dir);
    }

    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        foreach (var ch in text)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }
            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }
            current.Append(ch);
        }
        if (current.Length > 0)
            tokens.Add(current.ToString());
        return tokens;
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        var value = path.Trim().Trim('"').Trim();
        return value.Length == 0 ? null : value;
    }

    /// <summary>
    /// Абсолютный ли путь: «C:\...» / «C:/...», UNC «\\...» или POSIX «/...». Нужен и при
    /// кросс-сборке под Linux, где <see cref="Path.IsPathRooted"/> не распознаёт диск Windows.
    /// </summary>
    private static bool IsRootedPath(string path)
    {
        if (Path.IsPathRooted(path))
            return true;
        return path.Length >= 3
               && char.IsLetter(path[0])
               && path[1] == ':'
               && (path[2] == '\\' || path[2] == '/');
    }

    /// <summary>Ключ посещённого файла графа Include: без учёта регистра и разделителей пути.</summary>
    private static string NormalizeVisitKey(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;
        return path.Trim()
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar)
            .ToUpperInvariant();
    }

    private static string? ExtractDirective(string output, string name)
    {
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("-D ", StringComparison.Ordinal))
                continue;
            var rest = line.Substring(3).Trim();
            if (!rest.StartsWith(name + "=", StringComparison.Ordinal))
                continue;
            var value = rest.Substring(name.Length + 1).Trim().Trim('"');
            return value.Length == 0 ? null : value;
        }
        return null;
    }

    private static string? ExtractDocumentRoot(string output, string fallback)
    {
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            var idx = line.IndexOf("DocumentRoot", StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                continue;
            var colon = line.IndexOf(':', idx);
            var from = colon >= 0 ? colon + 1 : idx + "DocumentRoot".Length;
            var value = line.Substring(from).Trim().Trim('"');
            if (value.Length > 0)
                return value;
        }
        return fallback;
    }

    private static string? ExtractServerVersion(string output)
    {
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("Server version:", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Server Version:", StringComparison.OrdinalIgnoreCase))
            {
                var value = line.Substring(line.IndexOf(':') + 1).Trim();
                return value.Length == 0 ? null : value;
            }
        }
        return null;
    }

    /// <summary>Разбирает строку версии («Apache/2.4.62 (Ubuntu)») — возвращает старший номер.</summary>
    private static int ParseMajorVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return 0;
        var slash = version.IndexOf('/');
        if (slash < 0)
            return 0;
        var rest = version.Substring(slash + 1);
        var dot = rest.IndexOf('.');
        var majorText = dot > 0 ? rest.Substring(0, dot) : rest;
        return int.TryParse(majorText, out var major) ? major : 0;
    }

    /// <summary>
    /// Apache 2.2 — признак для webinst (ключ -apache22); 2.4 и новее — -apache24.
    /// Определяется по подстроке версии и по корню (C:\Apache22 / сборочный флаг).
    /// </summary>
    private static bool IsApache22(string? version, int major)
    {
        if (version is not null
            && version.IndexOf("Apache/2.2", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        return major == 2 && version is not null
            && version.IndexOf("Apache/2.4", StringComparison.OrdinalIgnoreCase) < 0;
    }

    private static bool LooksLikeApache22(string? serverRoot)
    {
        if (string.IsNullOrWhiteSpace(serverRoot))
            return false;
        return serverRoot.IndexOf("apache22", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value!;
        }
        return string.Empty;
    }
}