using System.IO;

namespace Workspace1C.Services;

/// <summary>
/// Сервис поиска установленных версий платформы 1С:Предприятие.
/// </summary>
public static class PlatformVersionService
{
    private static readonly object _extraRootsLock = new();
    private static List<string> _extraSearchRoots = new();

    // TTL-кэши результатов поиска каталогов платформ (issue #67: резолвер контекста
    // запуска сценариев вызывался на каждое обновление превью и перебор строк списка,
    // а рекурсивное сканирование Program Files на всех дисках занимало секунды —
    // «первый вход в редактор ~25 секунд», «пробел — 2-3 секунды тормозов»). Список
    // каталогов версий и результат резолва каталога по версии пересчитываются раз в
    // 5 минут; сброс — InvalidateSearchCache (смена дополнительных путей, установка
    // платформы). Результаты не модифицируются потребителями (только чтение/foreach).
    private const int SearchCacheTtlSeconds = 300;

    private static readonly object _searchCacheLock = new();
    private static readonly Dictionary<string, (DateTime At, List<(string Version, string BinDir)> Entries)> _platformVersionDirsCache = new();
    private static readonly Dictionary<(string Version, string Arch), (DateTime At, string? Result)> _resolvedVersionBinCache = new();

    /// <summary>
    /// Сбрасывает TTL-кэши поиска каталогов платформ (issue #67). Вызывается при смене
    /// дополнительных путей поиска и после установки/удаления платформы; без вызова кэш
    /// истекает сам за <see cref="SearchCacheTtlSeconds"/> секунд.
    /// </summary>
    public static void InvalidateSearchCache()
    {
        lock (_searchCacheLock)
        {
            _platformVersionDirsCache.Clear();
            _resolvedVersionBinCache.Clear();
        }
    }

    public static void SetAdditionalSearchPaths(IEnumerable<string>? paths)
    {
        lock (_extraRootsLock)
        {
            _extraSearchRoots = paths?
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();
        }
        // Корни поиска изменились — сбросить кэши (иначе новые пути станут видны через TTL).
        InvalidateSearchCache();
    }

    public static IReadOnlyList<string> GetAdditionalSearchPaths()
    {
        lock (_extraRootsLock)
        {
            return _extraSearchRoots.ToList();
        }
    }

    public static List<string> FindInstalledVersions(IEnumerable<string>? additionalPaths = null)
        => FindInstalledVersionInfos(additionalPaths).Select(v => v.Display).ToList();

    /// <summary>
    /// Возвращает установленные версии платформы вместе с путями к каталогам версий.
    /// </summary>
    public static List<Models.PlatformVersionInfo> FindInstalledVersionInfos(IEnumerable<string>? additionalPaths = null)
    {
        // Display → Path (при дубликатах оставляем первый найденный путь)
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Display версий, у которых установлен ТОЛЬКО тонкий клиент (в bin есть 1cv8c,
        // но нет полного 1cv8) — такие версии помечаются в дереве выбора (issue #33).
        var thinClients = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        AddVersionsFromRoot(map, thinClients, programFiles, "64");
        AddVersionsFromRoot(map, thinClients, programFilesX86, "32");

        // issue #361: версии, установленные на НЕсистемном диске (D:\Program Files\1cv8
        // и т.п.), не попадали в список установленных — строка «Есть обновление» на
        // последней версии своей линии (комментарий 7OH от 2026-10-11). Сканируем
        // стандартные каталоги Program Files на всех доступных логических дисках.
        foreach (var (root, arch) in EnumerateProgramFilesRootsOnAllDrives())
            AddVersionsFromRoot(map, thinClients, root, arch);

        IEnumerable<string> extra;
        if (additionalPaths != null)
        {
            extra = additionalPaths
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
        else
        {
            lock (_extraRootsLock)
            {
                extra = _extraSearchRoots.ToList();
            }
        }

        foreach (var path in extra)
            AddVersionsFromCustomPath(map, thinClients, path, programFiles, programFilesX86);

        // issue #361: отладочный флаг CM_DEBUG_PLATFORM_UPDATE=1 — найденные версии
        // с путями в platform-update-debug.log (диагностика «Есть обновление» для
        // последних версий линий: видно, какие версии реально найдены и откуда).
        if (PlatformUpdateDebug.IsEnabled)
        {
            PlatformUpdateDebug.Log($"[PlatformVersionService] Найдено установленных версий: {map.Count}");
            foreach (var kv in map.OrderByDescending(kv => kv.Key, new VersionComparer()))
                PlatformUpdateDebug.Log($"  {kv.Key} -> {kv.Value}");
        }

        return map
            .Select(kv => new Models.PlatformVersionInfo
            {
                Display = kv.Key,
                Path = kv.Value,
                IsThinClient = thinClients.Contains(kv.Key)
            })
            .OrderByDescending(v => v.Display, new VersionComparer())
            .ToList();
    }

    public static IReadOnlyList<string> GetSearchRoots(string architecture)
    {
        var roots = new List<string>();
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        if (architecture == "64" || architecture == "x64")
        {
            if (!string.IsNullOrEmpty(programFiles))
                roots.Add(programFiles);
        }
        else
        {
            if (!string.IsNullOrEmpty(programFilesX86))
                roots.Add(programFilesX86);
            if (roots.Count == 0 && !string.IsNullOrEmpty(programFiles))
                roots.Add(programFiles);
        }

        // issue #361: корни «Program Files» на всех дисках — платформа может стоять
        // на D:\, E:\ и т.п.; версии оттуда должны участвовать в поиске и списке.
        var all = EnumerateProgramFilesRootsOnAllDrives();
        if (architecture == "64" || architecture == "x64")
        {
            foreach (var (root, arch) in all)
            {
                if (arch == "64" && !roots.Contains(root, StringComparer.OrdinalIgnoreCase))
                    roots.Add(root);
            }
        }
        else
        {
            foreach (var (root, arch) in all)
            {
                if (arch == "32" && !roots.Contains(root, StringComparer.OrdinalIgnoreCase))
                    roots.Add(root);
            }
        }

        lock (_extraRootsLock)
        {
            foreach (var p in _extraSearchRoots)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                var root = ResolveRootFromCustomPath(p);
                if (!string.IsNullOrEmpty(root) && !roots.Contains(root, StringComparer.OrdinalIgnoreCase))
                    roots.Add(root);
            }
        }

        return roots;
    }

    /// <summary>
    /// Разрешает каталог <c>bin</c> указанной версии платформы нужной разрядности.
    /// Использует те же правила поиска, что и при составлении списка версий
    /// (стандартные Program Files + дополнительные папки с учётом нестандартной
    /// вложенности каталогов версий). Возвращает null, если версия не найдена.
    /// Полная версия (4 числовых сегмента, «8.3.27.1688») ищется точно.
    /// Частичная версия («8.5», «8.3.27») трактуется как префикс (issue #142):
    /// возвращается каталог новейшей установленной версии, соответствующей этому
    /// префиксу и нужной разрядности.
    /// </summary>
    public static string? ResolveVersionBinDirectory(string version, string architecture)
    {
        ParseVariant(version ?? string.Empty, out var cleanVersion, out _);
        if (string.IsNullOrWhiteSpace(cleanVersion))
            return null;

        var archKey = architecture == "64" ? "64" : "32";
        var cacheKey = (cleanVersion, archKey);

        lock (_searchCacheLock)
        {
            if (_resolvedVersionBinCache.TryGetValue(cacheKey, out var cached)
                && DateTime.UtcNow - cached.At < TimeSpan.FromSeconds(SearchCacheTtlSeconds))
                return cached.Result;
        }

        var result = ResolveVersionBinDirectoryCore(cleanVersion, archKey);
        lock (_searchCacheLock)
        {
            _resolvedVersionBinCache[cacheKey] = (DateTime.UtcNow, result);
        }

        return result;
    }

    private static string? ResolveVersionBinDirectoryCore(string cleanVersion, string archKey)
    {
        // Полная версия — точное разрешение каталога (обратная совместимость).
        if (CountVersionSegments(cleanVersion) >= 4)
        {
            foreach (var root in GetSearchRoots(archKey))
            {
                // Стандартный макет: <root>\1cv8\<ver>\bin
                var standard = Path.Combine(root, "1cv8", cleanVersion, "bin");
                if (IsVersionBin(standard, archKey))
                    return standard;

                // Нестандартный макет (дополнительные папки): рекурсивный поиск каталога версии.
                var flexible = FindVersionBinRecursive(root, cleanVersion, archKey, depth: 0, maxDepth: 6);
                if (flexible != null)
                    return flexible;
            }

            return null;
        }

        // Частичная версия — префиксный подбор новейшей установленной версии.
        return MatchLatestVersionByPrefix(cleanVersion, archKey);
    }

    /// <summary>
    /// Число числовых сегментов версии («8.3.27» → 3, «8.3.27.1688» → 4).
    /// Версия с менее чем 4 сегментами считается частичной (префиксом, issue #142).
    /// </summary>
    public static int CountVersionSegments(string version)
    {
        var parts = (version ?? string.Empty).Split('.', StringSplitOptions.RemoveEmptyEntries);
        var count = 0;
        foreach (var part in parts)
        {
            if (int.TryParse(part.Trim(), out _))
                count++;
            else
                break;
        }
        return count;
    }

    /// <summary>
    /// Подбирает каталог <c>bin</c> новейшей установленной версии, соответствующей
    /// частичному префиксу (например «8.3.27» → максимальная из всех 8.3.27.*)
    /// и нужной разрядности. Возвращает null, если ничего не найдено.
    /// </summary>
    private static string? MatchLatestVersionByPrefix(string prefix, string archKey)
    {
        string? bestDir = null;
        string bestVersion = string.Empty;

        foreach (var (verName, binDir) in FindPlatformVersionDirs(archKey))
        {
            if (!MatchesVersionPrefix(verName, prefix))
                continue;

            // Берём «наибольшую» версию по числовому сравнению сегментов
            // (8.3.10 > 8.3.9 — строковое сравнение давало бы неверный результат).
            if (bestDir is null || CompareVersions(verName, bestVersion) > 0)
            {
                bestDir = binDir;
                bestVersion = verName;
            }
        }

        return bestDir;
    }

    /// <summary>
    /// Проверяет, начинается ли фактическая версия с указанного префикса.
    /// «8.3.27.1688» соответствует префиксу «8.3.27», но не «8.5» или «8.3.2».
    /// </summary>
    private static bool MatchesVersionPrefix(string version, string prefix)
    {
        var vParts = (version ?? string.Empty).Split('.');
        var pParts = (prefix ?? string.Empty).Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (vParts.Length < pParts.Length)
            return false;

        for (var i = 0; i < pParts.Length; i++)
        {
            if (!string.Equals(vParts[i].Trim(), pParts[i].Trim(), StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Сравнение номеров версий 1С по числовым сегментам (8.3.24.1000). >0 если a новее b.
    /// </summary>
    private static int CompareVersions(string a, string b)
    {
        static int[] Parts(string v)
        {
            return (v ?? string.Empty)
                .Split(new[] { '.', ' ', '(' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(p => int.TryParse(p, out _))
                .Select(int.Parse)
                .ToArray();
        }

        var pa = Parts(a);
        var pb = Parts(b);
        var len = Math.Max(pa.Length, pb.Length);
        for (var i = 0; i < len; i++)
        {
            var va = i < pa.Length ? pa[i] : 0;
            var vb = i < pb.Length ? pb[i] : 0;
            if (va != vb)
                return va.CompareTo(vb);
        }
        return 0;
    }

    /// <summary>
    /// Возвращает все найденные каталоги версий нужной разрядности в виде пар
    /// (имя версии, каталог bin), включая стандартные и дополнительные корни с учётом
    /// нестандартной вложенности. Используется лаунчером для выбора новейшей версии.
    /// </summary>
    public static List<(string Version, string BinDir)> FindPlatformVersionDirs(string architecture)
    {
        var archKey = architecture == "64" ? "64" : "32";

        lock (_searchCacheLock)
        {
            if (_platformVersionDirsCache.TryGetValue(archKey, out var cached)
                && DateTime.UtcNow - cached.At < TimeSpan.FromSeconds(SearchCacheTtlSeconds))
                return cached.Entries;
        }

        var computed = ComputePlatformVersionDirs(archKey);
        lock (_searchCacheLock)
        {
            _platformVersionDirsCache[archKey] = (DateTime.UtcNow, computed);
        }

        return computed;
    }

    private static List<(string Version, string BinDir)> ComputePlatformVersionDirs(string archKey)
    {
        var results = new List<(string Version, string BinDir)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in GetSearchRoots(archKey))
        {
            AddVersionDirsFrom1cv8(results, seen, Path.Combine(root, "1cv8"), archKey);
            ScanAddVersionDirs(results, seen, root, archKey, depth: 0, maxDepth: 6);
        }

        return results;
    }

    private static bool IsVersionBin(string binDir, string archKey)
    {
        if (!Directory.Exists(binDir))
            return false;
        if (!Directory.EnumerateFiles(binDir, "1cv8*.exe").Any())
            return false;
        return archKey == "32" ? DetectArchitecture(binDir) == "32"
                               : DetectArchitecture(binDir) == "64";
    }

    private static string? FindVersionBinRecursive(string path, string version, string archKey, int depth, int maxDepth)
    {
        if (depth > maxDepth || !Directory.Exists(path))
            return null;

        try
        {
            foreach (var dir in Directory.GetDirectories(path))
            {
                var name = Path.GetFileName(dir);
                if (string.IsNullOrEmpty(name) || name.StartsWith(".", StringComparison.Ordinal))
                    continue;

                if (string.Equals(name, version, StringComparison.OrdinalIgnoreCase))
                {
                    var binDir = Path.Combine(dir, "bin");
                    if (IsVersionBin(binDir, archKey))
                        return binDir;
                }

                if (name.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("docs", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("readme", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("common", StringComparison.OrdinalIgnoreCase))
                    continue;

                var sub = FindVersionBinRecursive(dir, version, archKey, depth + 1, maxDepth);
                if (sub != null)
                    return sub;
            }
        }
        catch
        {
            /* нет доступа */
        }

        return null;
    }

    private static void AddVersionDirsFrom1cv8(
        List<(string Version, string BinDir)> results,
        HashSet<string> seen,
        string baseDir,
        string archKey)
    {
        if (!Directory.Exists(baseDir))
            return;

        foreach (var dir in Directory.GetDirectories(baseDir))
        {
            var name = Path.GetFileName(dir);
            if (!IsVersionDirectory(name))
                continue;

            var binDir = Path.Combine(dir, "bin");
            if (!IsVersionBin(binDir, archKey))
                continue;

            if (seen.Add(binDir))
                results.Add((name, binDir));
        }
    }

    private static void ScanAddVersionDirs(
        List<(string Version, string BinDir)> results,
        HashSet<string> seen,
        string path,
        string archKey,
        int depth,
        int maxDepth)
    {
        if (depth > maxDepth || !Directory.Exists(path))
            return;

        try
        {
            foreach (var dir in Directory.GetDirectories(path))
            {
                var name = Path.GetFileName(dir);
                if (string.IsNullOrEmpty(name) || name.StartsWith(".", StringComparison.Ordinal))
                    continue;

                if (IsVersionDirectory(name))
                {
                    var binDir = Path.Combine(dir, "bin");
                    if (IsVersionBin(binDir, archKey) && seen.Add(binDir))
                        results.Add((name, binDir));
                }

                if (name.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("docs", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("readme", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("common", StringComparison.OrdinalIgnoreCase))
                    continue;

                ScanAddVersionDirs(results, seen, dir, archKey, depth + 1, maxDepth);
            }
        }
        catch
        {
            /* нет доступа */
        }
    }

    /// <summary>
    /// Стандартные каталоги <c>Program Files</c> и <c>Program Files (x86)</c> на всех
    /// доступных логических дисках Windows (issue #361: версии на несистемном диске
    /// раньше не находились). Каталоги, совпадающие с системными корнями, дублируются —
    /// агрегация по версии (map) устойчива, а GetSearchRoots дедуплицирует сам.
    /// </summary>
    private static IEnumerable<(string Root, string Arch)> EnumerateProgramFilesRootsOnAllDrives()
    {
        var result = new List<(string, string)>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                string driveRoot;
                try
                {
                    if (!drive.IsReady)
                        continue;
                    driveRoot = drive.RootDirectory.FullName;
                }
                catch (IOException)
                {
                    continue; // диск отвалился/CD-ROM без носителя
                }

                result.Add((Path.Combine(driveRoot, "Program Files"), "64"));
                result.Add((Path.Combine(driveRoot, "Program Files (x86)"), "32"));
            }
        }
        catch (IOException)
        {
            // Перечисление дисков недоступно (редкий случай) — работаем с системными корнями.
        }
        catch (UnauthorizedAccessException)
        {
            // Нет прав на перечисление дисков.
        }
        return result;
    }

    private static void TryAddVersion(
        Dictionary<string, string> map,
        HashSet<string> thinClients,
        string display,
        string versionDir,
        bool isThinClient)
    {
        if (!map.ContainsKey(display))
        {
            map[display] = versionDir;
            if (isThinClient)
                thinClients.Add(display);
        }
    }

    private static void AddVersionsFromRoot(
        Dictionary<string, string> map,
        HashSet<string> thinClients,
        string? root,
        string architecture)
    {
        if (string.IsNullOrEmpty(root))
            return;

        var baseDir = Path.Combine(root, "1cv8");
        if (!Directory.Exists(baseDir))
            return;

        AddVersionsFrom1cv8Dir(map, thinClients, baseDir, architecture);
    }

    private static void AddVersionsFromCustomPath(
        Dictionary<string, string> map,
        HashSet<string> thinClients,
        string path,
        string? programFiles,
        string? programFilesX86)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;

        string? preferredArch = null;
        if (!string.IsNullOrEmpty(programFilesX86) &&
            path.StartsWith(programFilesX86, StringComparison.OrdinalIgnoreCase))
            preferredArch = "32";
        else if (!string.IsNullOrEmpty(programFiles) &&
                 path.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase))
            preferredArch = "64";

        // E:\1cPlatform\1cv8\8.3.x  или  E:\1cPlatform\8.3.x
        var oneCDir = Path.Combine(path, "1cv8");
        if (Directory.Exists(oneCDir))
        {
            AddVersionsFrom1cv8Dir(map, thinClients, oneCDir, preferredArch);
            // также рекурсивно — на случай вложенных копий
            ScanForVersionDirectories(map, thinClients, path, preferredArch, depth: 0, maxDepth: 4);
            return;
        }

        if (HasVersionSubdirectories(path) ||
            string.Equals(Path.GetFileName(path), "1cv8", StringComparison.OrdinalIgnoreCase))
        {
            AddVersionsFrom1cv8Dir(map, thinClients, path, preferredArch);
            ScanForVersionDirectories(map, thinClients, path, preferredArch, depth: 0, maxDepth: 3);
            return;
        }

        var binDir = Path.Combine(path, "bin");
        if (Directory.Exists(binDir) && IsVersionDirectory(Path.GetFileName(path)))
        {
            var arch = preferredArch ?? DetectArchitecture(binDir);
            if (File.Exists(Path.Combine(binDir, "1cv8.exe")) ||
                File.Exists(Path.Combine(binDir, "1cv8x64.exe")))
            {
                TryAddVersion(map, thinClients, FormatVariant(Path.GetFileName(path), arch), path, false);
            }
            else if (IsThinClientBin(binDir))
            {
                TryAddVersion(map, thinClients, FormatVariant(Path.GetFileName(path), arch), path, true);
            }
            return;
        }

        // Произвольный корень (например E:\1cPlatform) — ищем версии рекурсивно
        ScanForVersionDirectories(map, thinClients, path, preferredArch, depth: 0, maxDepth: 5);
    }

    /// <summary>
    /// Рекурсивный поиск каталогов вида 8.3.x.x с bin\1cv8.exe (нестандартные корни установки).
    /// </summary>
    private static void ScanForVersionDirectories(
        Dictionary<string, string> map,
        HashSet<string> thinClients,
        string path,
        string? preferredArchitecture,
        int depth,
        int maxDepth)
    {
        if (depth > maxDepth || !Directory.Exists(path))
            return;

        try
        {
            foreach (var dir in Directory.GetDirectories(path))
            {
                var name = Path.GetFileName(dir);
                if (string.IsNullOrEmpty(name) || name.StartsWith(".", StringComparison.Ordinal))
                    continue;

                if (IsVersionDirectory(name))
                {
                    var binDir = Path.Combine(dir, "bin");
                    if (Directory.Exists(binDir) &&
                        (File.Exists(Path.Combine(binDir, "1cv8.exe")) ||
                         File.Exists(Path.Combine(binDir, "1cv8x64.exe"))))
                    {
                        var arch = preferredArchitecture ?? DetectArchitecture(binDir);
                        TryAddVersion(map, thinClients, FormatVariant(name, arch), dir, false);
                    }
                    else if (IsThinClientBin(binDir))
                    {
                        var arch = preferredArchitecture ?? DetectArchitecture(binDir);
                        TryAddVersion(map, thinClients, FormatVariant(name, arch), dir, true);
                    }
                }

                // не заходим в bin/docs и т.п.
                if (name.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("docs", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("readme", StringComparison.OrdinalIgnoreCase))
                    continue;

                ScanForVersionDirectories(map, thinClients, dir, preferredArchitecture, depth + 1, maxDepth);
            }
        }
        catch
        {
            /* нет доступа */
        }
    }

    private static void AddVersionsFrom1cv8Dir(
        Dictionary<string, string> map,
        HashSet<string> thinClients,
        string baseDir,
        string? preferredArchitecture)
    {
        if (!Directory.Exists(baseDir))
            return;

        foreach (var dir in Directory.GetDirectories(baseDir))
        {
            var name = Path.GetFileName(dir);
            if (!IsVersionDirectory(name))
                continue;

            var binDir = Path.Combine(dir, "bin");
            if (!Directory.Exists(binDir))
                continue;

            if (File.Exists(Path.Combine(binDir, "1cv8.exe")) ||
                File.Exists(Path.Combine(binDir, "1cv8x64.exe")))
            {
                var arch = preferredArchitecture ?? DetectArchitecture(binDir);
                TryAddVersion(map, thinClients, FormatVariant(name, arch), dir, false);
            }
            else if (IsThinClientBin(binDir))
            {
                // Тонкий клиент (issue #33): в bin есть 1cv8c.exe, но нет полного 1cv8.exe —
                // версия должна попадать в список выбора платформы и помечаться.
                var arch = preferredArchitecture ?? DetectArchitecture(binDir);
                TryAddVersion(map, thinClients, FormatVariant(name, arch), dir, true);
            }
        }
    }

    /// <summary>
    /// True — в каталоге bin установлен ТОЛЬКО тонкий клиент: есть 1cv8c.exe,
    /// но нет полного 1cv8.exe/1cv8x64.exe (issue #33). Такая версия не даёт
    /// полных возможностей (конфигуратор, сервер и т.п.).
    /// </summary>
    private static bool IsThinClientBin(string binDir)
    {
        if (!Directory.Exists(binDir))
            return false;
        if (File.Exists(Path.Combine(binDir, "1cv8.exe")) ||
            File.Exists(Path.Combine(binDir, "1cv8x64.exe")))
            return false;
        return File.Exists(Path.Combine(binDir, "1cv8c.exe"));
    }

    /// <summary>
    /// Определяет разрядность по имени exe и PE-заголовку (Machine).
    /// </summary>
    private static string DetectArchitecture(string binDir)
    {
        var x64 = Path.Combine(binDir, "1cv8x64.exe");
        if (File.Exists(x64))
            return "64";

        var exe = Path.Combine(binDir, "1cv8.exe");
        if (!File.Exists(exe))
            exe = Path.Combine(binDir, "1cv8c.exe"); // тонкий клиент (issue #33)
        if (!File.Exists(exe))
            return "64";

        try
        {
            using var fs = File.OpenRead(exe);
            if (fs.Length < 0x40) return "64";
            using var br = new BinaryReader(fs);
            // MZ
            if (br.ReadUInt16() != 0x5A4D) return "64";
            fs.Seek(0x3C, SeekOrigin.Begin);
            var peOffset = br.ReadInt32();
            if (peOffset <= 0 || peOffset + 6 >= fs.Length) return "64";
            fs.Seek(peOffset, SeekOrigin.Begin);
            if (br.ReadUInt32() != 0x00004550) return "64"; // PE\0\0
            var machine = br.ReadUInt16();
            // IMAGE_FILE_MACHINE_I386 = 0x14c, AMD64 = 0x8664
            if (machine == 0x014c) return "32";
            if (machine == 0x8664) return "64";
        }
        catch
        {
            /* ignore */
        }

        return "64";
    }

    private static bool HasVersionSubdirectories(string path)
    {
        try
        {
            return Directory.GetDirectories(path).Any(d => IsVersionDirectory(Path.GetFileName(d)));
        }
        catch
        {
            return false;
        }
    }

    private static string? ResolveRootFromCustomPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return null;

        if (Directory.Exists(Path.Combine(path, "1cv8")))
            return path;

        if (string.Equals(Path.GetFileName(path), "1cv8", StringComparison.OrdinalIgnoreCase) ||
            HasVersionSubdirectories(path))
        {
            var parent = Path.GetDirectoryName(path);
            return string.IsNullOrEmpty(parent) ? path : parent;
        }

        if (IsVersionDirectory(Path.GetFileName(path)) &&
            Directory.Exists(Path.Combine(path, "bin")))
        {
            var oneC = Path.GetDirectoryName(path);
            var root = string.IsNullOrEmpty(oneC) ? null : Path.GetDirectoryName(oneC);
            return root ?? oneC ?? path;
        }

        return path;
    }

    public static string FormatVariant(string version, string architecture)
        => $"{version} ({architecture})";

    public static void ParseVariant(string variant, out string version, out string architecture)
    {
        version = variant;
        architecture = "32";

        if (string.IsNullOrWhiteSpace(variant))
            return;

        var end = variant.LastIndexOf(')');
        var start = variant.LastIndexOf('(');
        if (end < 0 || start < 0 || start > end)
            return;

        var arch = variant.Substring(start + 1, end - start - 1).Trim();
        if (arch == "64" || arch == "32")
        {
            version = variant.Substring(0, start).Trim();
            architecture = arch;
        }
    }

    /// <summary>
    /// Возвращает true, если в варианте разрядность задана явно суффиксом «(32)»/«(64)».
    /// <see cref="ParseVariant"/> по умолчанию возвращает «32» даже без суффикса, поэтому выбор
    /// папки/частичной версии («8.3», «8.3.27») не должен подставлять разрядность x86, пока она
    /// не задана явно (issue #251). Разрешение разрядности при этом остаётся за лаунчером.
    /// </summary>
    public static bool HasExplicitArchitecture(string variant)
    {
        if (string.IsNullOrWhiteSpace(variant))
            return false;
        return variant.Contains("(32)", StringComparison.OrdinalIgnoreCase)
            || variant.Contains("(64)", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Разбирает строку варианта «8.3.27.2214 (64)», отделяя необязательную разрядность.
    /// В отличие от <see cref="ParseVariant"/> возвращает architecture == null, если разрядность
    /// НЕ задана явно (варианты «8.3», «8.3.27»): ParseVariant по умолчанию считает их x32,
    /// из-за чего такие версии «исчезали» из фильтра x64 и ломали поиск узла (issue #304).
    /// Распознаются «(64)»/«(x64)» и «(32)»/«(x32)»/«(x86)».
    /// </summary>
    public static void ParseVariantOptionalArch(string variant, out string version, out string? architecture)
    {
        version = variant;
        architecture = null;

        if (string.IsNullOrWhiteSpace(variant))
            return;

        var end = variant.LastIndexOf(')');
        var start = variant.LastIndexOf('(');
        if (end < 0 || start < 0 || start > end)
            return;

        var arch = variant.Substring(start + 1, end - start - 1).Trim().ToLowerInvariant();
        string? resolved = arch switch
        {
            "64" or "x64" => "64",
            "32" or "x32" or "x86" => "32",
            _ => null
        };
        if (resolved is null)
            return;

        version = variant.Substring(0, start).Trim();
        architecture = resolved;
    }

    /// <summary>
    /// Линия платформы — первые два числа версии: «8.3.27.1688 (64)» → «8.3».
    /// Группировка по двум цифрам (issue #9): берём первые два числовых сегмента,
    /// чтобы даже нестандартный вариант (с нечисловым сегментом) попадал в свою линию.
    /// </summary>
    public static string GetVersionLine(string variant)
    {
        ParseVariant(variant, out var version, out _);
        var parts = version.Split('.');
        var numerics = parts.Where(p => int.TryParse(p, out _)).Take(2).ToList();
        if (numerics.Count == 2)
            return string.Join(".", numerics);
        return parts.Length >= 2
            ? string.Join(".", parts.Take(2))
            : (string.IsNullOrEmpty(version) ? "—" : version);
    }

    /// <summary>
    /// Группа сборки — первые три числа: «8.3.27.1688 (64)» → «8.3.27».
    /// </summary>
    public static string GetVersionBuildGroup(string variant)
    {
        ParseVariant(variant, out var version, out _);
        var parts = version.Split('.');
        return parts.Length >= 3
            ? string.Join(".", parts.Take(3))
            : GetVersionLine(variant);
    }

    /// <summary>
    /// Подпись разрядности в стиле стартера 1С: «x64» / «x32».
    /// </summary>
    public static string FormatArchitectureLabel(string? architecture)
    {
        if (string.IsNullOrWhiteSpace(architecture)) return "";
        var a = architecture.Trim();
        if (a is "64" or "x64" or "X64") return "x64";
        if (a is "32" or "x32" or "X32" or "x86") return "x32";
        return a;
    }

    /// <summary>
    /// Дерево выбора платформы (как в стартере 1С):
    /// линия (8.3) → группа сборок (8.3.27) → полная версия «8.3.27.2214 (x64)».
    /// При активном фильтре разрядности скрываются только листья другой разрядности,
    /// а папки (линии и группы сборок) сохраняются, если в линии остался хотя бы один
    /// видимый вариант: выбор папки «8.5.1» не должен «перескакивать» на линию 8.5 при
    /// переключении фильтра, пока семейство 8.5 хоть как-то видимо (issue #304).
    /// </summary>
    public static List<Models.PlatformVersionGroup> BuildGroupedTree(
        IEnumerable<Models.PlatformVersionInfo> infos, string archFilter = "all")
    {
        var list = infos?.ToList() ?? new List<Models.PlatformVersionInfo>();
        var roots = new List<Models.PlatformVersionGroup>();

        var byLine = list
            .GroupBy(i => GetVersionLine(i.Display))
            .OrderByDescending(g => g.Key, new VersionComparer());

        foreach (var lineGroup in byLine)
        {
            // Линия остаётся в дереве, только если в ней есть хотя бы один лист,
            // проходящий фильтр разрядности. Иначе папка «8.5» пуста — показывать
            // нечего, fallback не нужен (issue #304).
            if (!lineGroup.Any(i => PassesArchFilter(i.Display, archFilter)))
                continue;

            var lineNode = new Models.PlatformVersionGroup { Name = lineGroup.Key, Kind = Models.PlatformNodeKind.Line };

            var byBuild = lineGroup
                .GroupBy(i => GetVersionBuildGroup(i.Display))
                .OrderByDescending(g => g.Key, new VersionComparer());

            foreach (var buildGroup in byBuild)
            {
                // Папка группы сборок («8.5.1») сохраняется даже без собственных
                // видимых листьев: линия видима, а выбранная папка должна оставаться
                // доступной при переключении разрядности (issue #304).
                var buildNode = new Models.PlatformVersionGroup { Name = buildGroup.Key, Kind = Models.PlatformNodeKind.BuildGroup };

                foreach (var info in buildGroup.OrderByDescending(i => i.Display, new VersionComparer()))
                {
                    if (!PassesArchFilter(info.Display, archFilter))
                        continue;

                    ParseVariant(info.Display, out var version, out var arch);
                    var archLabel = FormatArchitectureLabel(arch);
                    var leafName = string.IsNullOrEmpty(archLabel)
                        ? version
                        : $"{version} ({archLabel})";

                    var leafKind = archLabel switch
                    {
                        "x64" => Models.PlatformNodeKind.LeafX64,
                        "x32" => Models.PlatformNodeKind.LeafX32,
                        _ => Models.PlatformNodeKind.Leaf
                    };
                    buildNode.Children.Add(new Models.PlatformVersionGroup
                    {
                        Name = leafName,
                        Path = info.Path,
                        Variant = info.Display,
                        Kind = leafKind,
                        IsThinClient = info.IsThinClient,
                        Versions = { info }
                    });
                    buildNode.Versions.Add(info);
                }

                lineNode.Children.Add(buildNode);
                lineNode.Versions.AddRange(buildNode.Versions);
            }

            roots.Add(lineNode);
        }

        return roots;
    }

    /// <summary>
    /// Ищет узел дерева, соответствующий текущей версии. Полной версии (4+ частей) — точный
    /// лист с учётом разрядности; частичной версии («8.5» / «8.5.1») — сама «папка»
    /// (линия/группа сборок), а не максимальная сборка в ней (issue #251). Если выбранный узел
    /// отфильтрован (например, при переключении фильтра разрядности), делается fallback на
    /// доступный узел того же семейства: лист той же версии → группа сборок → линия, чтобы
    /// выделение не «скакало» и результат диалога не терялся (issue #304).
    /// Версия группы сборок с явной разрядностью («8.5.1 (64)») сначала ищется как точный
    /// лист: после переключения фильтра на ту же разрядность выделение остаётся на листе,
    /// а не на папке (issue #304).
    /// </summary>
    public static Models.PlatformVersionGroup? FindBestNode(
        IEnumerable<Models.PlatformVersionGroup> nodes, string currentVersion)
    {
        if (string.IsNullOrWhiteSpace(currentVersion)) return null;

        ParseVariantOptionalArch(currentVersion, out var version, out var architecture);
        var parts = version.Split('.', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length >= 4)
        {
            // Полная версия — точный лист (версия + разрядность, #304).
            var exact = FindExactLeaf(nodes, currentVersion);
            if (exact is not null) return exact;

            // Лист отфильтрован (другая разрядность) — лист той же версии любой разрядности.
            var any = FindExactLeaf(nodes, version);
            if (any is not null) return any;

            // Либо папка группы сборок этой версии.
            return FindBuildGroup(nodes, string.Join(".", parts.Take(3)));
        }

        var linePrefix = string.Join(".", parts.Take(2));
        var line = FindLine(nodes, linePrefix);
        if (line is null) return null;

        if (parts.Length == 3)
        {
            // Группа сборок «8.5.1» → сама папка группы. Папка сохраняется в дереве при
            // фильтре разрядности, пока линия видима (см. BuildGroupedTree, #304), поэтому
            // fallback на линию срабатывает только когда версий 8.5.1.* нет в данных вовсе.
            var groupKey = string.Join(".", parts.Take(3));

            // Разрядность задана явно («8.5.1 (64)») — сначала точный лист этой версии:
            // лист отображается в дереве, когда у 8.5.1 есть сборка нужной разрядности.
            if (architecture != null)
            {
                var exact = FindExactLeaf(nodes, currentVersion);
                if (exact is not null) return exact;
            }

            return FindBuildGroup(line, groupKey) ?? line;
        }

        // Только линия «8.5» → сама папка линии.
        return line;
    }

    /// <summary>Ищет узел линии с указанным префиксом («8.3»).</summary>
    private static Models.PlatformVersionGroup? FindLine(
        IEnumerable<Models.PlatformVersionGroup> nodes, string prefix)
        => nodes.FirstOrDefault(n =>
            !n.IsLeaf && string.Equals(n.Name, prefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>Ищет группу сборок с указанным префиксом среди детей узла (линии).</summary>
    private static Models.PlatformVersionGroup? FindBuildGroup(
        Models.PlatformVersionGroup node, string prefix)
        => node.Children.FirstOrDefault(n =>
            !n.IsLeaf && string.Equals(n.Name, prefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Ищет узел группы сборок с указанным префиксом («8.3.27») среди переданных узлов.
    /// </summary>
    private static Models.PlatformVersionGroup? FindBuildGroup(
        IEnumerable<Models.PlatformVersionGroup> nodes, string prefix)
    {
        foreach (var n in nodes)
        {
            if (!n.IsLeaf && string.Equals(n.Name, prefix, StringComparison.OrdinalIgnoreCase))
                return n;
            var found = FindBuildGroup(n.Children, prefix);
            if (found is not null) return found;
        }
        return null;
    }

    /// <summary>
    /// Ищет лист, соответствующий строке версии (с учётом разрядности, см. <see cref="MatchesCurrent"/>).
    /// </summary>
    public static Models.PlatformVersionGroup? FindExactLeaf(
        IEnumerable<Models.PlatformVersionGroup> nodes, string currentVersion)
    {
        foreach (var n in nodes)
        {
            if (n.IsLeaf && MatchesCurrent(n.Variant ?? n.Name, currentVersion))
                return n;
            var found = FindExactLeaf(n.Children, currentVersion);
            if (found is not null) return found;
        }
        return null;
    }

    /// <summary>
    /// Совпадает ли вариант листа с искомой строкой версии. Версия сравнивается всегда,
    /// а разрядность — только если она задана явно в искомой строке: «8.5.1 (32)» должен
    /// соответствовать листу x32, а «8.5.1» — любому листу той же версии (issue #251/#304).
    /// </summary>
    public static bool MatchesCurrent(string variant, string currentVersion)
    {
        if (string.IsNullOrWhiteSpace(currentVersion)) return false;
        if (string.Equals(variant?.Trim(), currentVersion.Trim(), StringComparison.OrdinalIgnoreCase))
            return true;

        ParseVariantOptionalArch(variant ?? string.Empty, out var version, out var arch);
        ParseVariantOptionalArch(currentVersion, out var cur, out var curArch);
        if (!string.Equals(version.Trim(), cur.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;

        return curArch is null || string.Equals(arch, curArch, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Формирует строку выбора для узла дерева — результат диалога. Лист отдаёт полный вариант
    /// («8.3.27.1688 (64)»), узел линии/группы сборок — частичную версию с суффиксом разрядности,
    /// если он однозначен (issue #142). Суффикс берётся из активного фильтра ТОЛЬКО когда в узле
    /// есть видимый лист этой разрядности: пустая при фильтре папка «8.5.1» (у версии нет x64-
    /// сборок) даёт чистую «8.5.1», а не несуществующую «8.5.1 (64)» (issue #304).
    /// </summary>
    public static string BuildResultVariant(Models.PlatformVersionGroup node, string archFilter)
    {
        if (node.IsLeaf && !string.IsNullOrEmpty(node.Variant))
            return node.Variant!;

        var name = node.Name ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var archSuffix = GetNodeArchSuffix(node, archFilter);
        return string.IsNullOrEmpty(archSuffix) ? name : $"{name} ({archSuffix})";
    }

    /// <summary>
    /// Суффикс разрядности для частичной версии. В режиме «Все» (авто) разрядность не
    /// подставляем — выбор папки/линии дерева даёт чистую версию без суффикса (issue #251),
    /// разрядность разрешается при запуске. При активном фильтре x32/x64 суффикс добавляется
    /// только если в узле реально есть видимый лист этой разрядности (issue #304).
    /// </summary>
    private static string? GetNodeArchSuffix(Models.PlatformVersionGroup node, string archFilter)
    {
        if (archFilter == "all") return null;

        var kind = archFilter == "x64"
            ? Models.PlatformNodeKind.LeafX64
            : Models.PlatformNodeKind.LeafX32;
        if (!HasVisibleLeafOfKind(node, kind))
            return null;

        return archFilter == "x64" ? "64" : "32";
    }

    private static bool HasVisibleLeafOfKind(Models.PlatformVersionGroup node, Models.PlatformNodeKind kind)
    {
        foreach (var child in node.Children)
        {
            if (child.IsLeaf && child.Kind == kind)
                return true;
            if (HasVisibleLeafOfKind(child, kind))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Фильтрует установленные версии по разрядности. Варианты БЕЗ явного суффикса («8.3.27»,
    /// частичная версия) показываются в обоих фильтрах: их разрядность неизвестна — ParseVariant
    /// считает такие x32, но это лишь значение по умолчанию, иначе выбранная версия «исчезала»
    /// из фильтра x64 и выделение «скакало» (issue #304, #251).
    /// </summary>
    public static List<Models.PlatformVersionInfo> FilterByArchitecture(
        IEnumerable<Models.PlatformVersionInfo> infos, string filter)
        => infos.Where(i => PassesArchFilter(i.Display, filter)).ToList();

    /// <summary>
    /// Проходит ли вариант листа активный фильтр разрядности. Варианты БЕЗ явного суффикса
    /// («8.3.27», частичная версия) проходят в обоих фильтрах: их разрядность неизвестна,
    /// решать должен лаунчер (issue #251/#304).
    /// </summary>
    private static bool PassesArchFilter(string display, string filter)
    {
        if (filter == "all") return true;
        if (!HasExplicitArchitecture(display)) return true;

        ParseVariant(display, out _, out var arch);
        var label = FormatArchitectureLabel(arch);
        return filter == "x64" ? label == "x64" : label == "x32";
    }

    private static bool IsVersionDirectory(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var parts = name.Split('.');
        if (parts.Length < 3 || parts.Length > 4)
            return false;

        foreach (var part in parts)
        {
            if (!int.TryParse(part, out _))
                return false;
        }

        return true;
    }

    private sealed class VersionComparer : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            if (x == y) return 0;
            if (x is null) return -1;
            if (y is null) return 1;

            var result = CompareCore(x, y);
            if (result != 0)
                return result;

            return GetArch(x).CompareTo(GetArch(y));
        }

        private static int CompareCore(string x, string y)
        {
            var xParts = CleanVersion(x).Split('.').Select(int.Parse).ToArray();
            var yParts = CleanVersion(y).Split('.').Select(int.Parse).ToArray();

            var length = Math.Max(xParts.Length, yParts.Length);
            for (var i = 0; i < length; i++)
            {
                var xVal = i < xParts.Length ? xParts[i] : 0;
                var yVal = i < yParts.Length ? yParts[i] : 0;
                if (xVal != yVal)
                    return xVal.CompareTo(yVal);
            }

            return 0;
        }

        private static string CleanVersion(string variant)
        {
            ParseVariant(variant, out var version, out _);
            return version;
        }

        private static int GetArch(string variant)
        {
            ParseVariant(variant, out _, out var architecture);
            return architecture == "64" ? 1 : 0;
        }
    }
}
