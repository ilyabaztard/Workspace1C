#if LINUX
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Workspace1C.Models;

namespace Workspace1C.Services
{
    /// <summary>
    /// Поиск и разбор установленных версий платформы 1С на Linux (Этап 5).
    /// Ищет каталоги установки в /opt/1cv8, /opt/1cv8.x86_64, ~/.1cv8,
    /// /usr/share/1cv8, /usr/bin и дополнительных папках.
    /// Бинарники — БЕЗ расширения .exe: 1cv8, 1cv8c, 1cv8s, 1cv8a, ragent.
    /// Разрядность определяется по каталогу и/или ELF-классу бинарника (readelf).
    /// </summary>
    public static class PlatformVersionService
    {
        private static readonly object _extraRootsLock = new();
        private static List<string> _additionalPaths = new();

        // TTL-кэши результатов поиска каталогов платформ (issue #67, зеркально Windows-версии:
        // резолвер контекста запуска сценариев вызывался на каждое обновление превью, а
        // сканирование каталогов установки занимало время). Пересчёт раз в 5 минут; сброс —
        // InvalidateSearchCache (смена дополнительных путей, установка платформы).
        private const int SearchCacheTtlSeconds = 300;

        private static readonly object _searchCacheLock = new();
        private static readonly Dictionary<string, (DateTime At, List<(string Version, string BinDir)> Entries)> _platformVersionDirsCache = new();
        private static readonly Dictionary<(string Version, string Arch), (DateTime At, string? Result)> _resolvedVersionBinCache = new();

        /// <summary>
        /// Сбрасывает TTL-кэши поиска каталогов платформ (issue #67). Вызывается при смене
        /// дополнительных путей поиска; без вызова кэш истекает сам за
        /// <see cref="SearchCacheTtlSeconds"/> секунд.
        /// </summary>
        public static void InvalidateSearchCache()
        {
            lock (_searchCacheLock)
            {
                _platformVersionDirsCache.Clear();
                _resolvedVersionBinCache.Clear();
            }
        }

        /// <summary>Имена исполняемых файлов платформы 1С на Linux (без .exe).</summary>
        public static readonly string[] OneCBinaryNames =
        {
            "1cv8", "1cv8c", "1cv8s", "1cv8a", "ragent", "rmngr", "rphost"
        };

        /// <summary>Стандартные корни установки платформы 1С на Linux.</summary>
        private static readonly string[] DefaultRoots =
        {
            "/opt/1cv8",
            "/opt/1cv8.x86_64",
            "/opt/1cv8.x86",
            "/usr/share/1cv8",
            "/usr/local/1cv8",
            "/usr/bin"
        };

        public static void SetAdditionalSearchPaths(IEnumerable<string>? paths)
        {
            lock (_extraRootsLock)
            {
                _additionalPaths = paths?
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Select(p => p.Trim().TrimEnd('/', '\\'))
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
                return _additionalPaths.ToList();
            }
        }

        /// <summary>Корни поиска установки 1С для указанной разрядности.</summary>
        private static IEnumerable<string> GetInstallRoots(string architecture)
        {
            var roots = new List<string>(DefaultRoots);

            var is64 = architecture == "64" || architecture == "x64";
            if (is64)
            {
                roots.Add("/opt/1cv8/x86_64");
                roots.Add("/opt/1cv8/amd64");
            }
            else
            {
                roots.Add("/opt/1cv8/i386");
                roots.Add("/opt/1cv8/i686");
            }

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home))
            {
                roots.Add(Path.Combine(home, ".1cv8"));
                roots.Add(Path.Combine(home, "1cv8"));
                roots.Add(Path.Combine(home, ".local", "share", "1cv8"));
            }

            lock (_extraRootsLock)
            {
                roots.AddRange(_additionalPaths);
            }

            return roots
                .Select(r => r.Trim().TrimEnd('/', '\\'))
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Корни bin для указанной разрядности (только существующие).</summary>
        public static IReadOnlyList<string> GetSearchRoots(string architecture)
        {
            var roots = new List<string>();
            foreach (var root in GetInstallRoots(architecture))
            {
                var bin = ResolveBinDirectory(root);
                if (bin is not null)
                    roots.Add(bin);
            }
            return roots;
        }

        /// <summary>Находит установленные версии платформы 1С на Linux.</summary>
        public static List<PlatformVersionInfo> FindInstalledVersionInfos(IEnumerable<string>? additionalPaths = null)
        {
            var result = new List<PlatformVersionInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var roots = new List<string>(GetInstallRoots("64"));
            if (additionalPaths != null)
            {
                roots.AddRange(additionalPaths.Where(p => !string.IsNullOrWhiteSpace(p)));
            }

            foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
                ScanRoot(root, result, seen);

            return result
                .OrderByDescending(v => v.Display, new VersionDisplayComparer())
                .ToList();
        }

        public static List<string> FindInstalledVersions(IEnumerable<string>? additionalPaths = null)
            => FindInstalledVersionInfos(additionalPaths).Select(v => v.Display).ToList();

        /// <summary>Все каталоги версий нужной разрядности как пары (версия, bin).</summary>
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

            foreach (var info in FindInstalledVersionInfos())
            {
                ParseVariant(info.Display, out var ver, out var arch);
                if (string.IsNullOrWhiteSpace(ver))
                    continue;
                var a = arch is "64" or "32" ? arch : DetectArchitecture(info.Path);
                if (a != archKey)
                    continue;
                var bin = ResolveBinDirectory(info.Path);
                if (bin is not null && seen.Add(bin))
                    results.Add((ver, bin));
            }

            return results;
        }

        /// <summary>
        /// Каталог bin для указанной версии и разрядности (если найден), иначе null.
        /// Полная версия (4 числовых сегмента, «8.3.27.1688») ищется точно.
        /// Частичная версия («8.5», «8.3.27») трактуется как префикс (issue #142):
        /// возвращается каталог новейшей установленной версии, соответствующей этому
        /// префиксу и нужной разрядности. Поиск охватывает ВСЕ корни: стандартные и
        /// дополнительные папки из настроек.
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
                foreach (var root in GetInstallRoots(archKey))
                {
                    var bin = ResolveBinDirectory(Path.Combine(root, cleanVersion));
                    if (bin is not null)
                        return bin;
                }

                // Ищем рекурсивно по дополнительным папкам (нестандартная вложенность).
                lock (_extraRootsLock)
                {
                    foreach (var root in _additionalPaths)
                    {
                        var found = FindVersionBinRecursive(root, cleanVersion, depth: 0, maxDepth: 6);
                        if (found != null)
                            return found;
                    }
                }

                return null;
            }

            // Частичная версия — префиксный подбор новейшей установленной версии (issue #142).
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
        /// Поиск ведётся по всем корням (стандартным и дополнительным), т.к.
        /// <see cref="FindPlatformVersionDirs"/> учитывает их все.
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
                if (bestDir is null || CompareVersionStrings(verName, bestVersion) > 0)
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

        private static string? FindVersionBinRecursive(string path, string version, int depth, int maxDepth)
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
                        var bin = ResolveBinDirectory(dir);
                        if (bin is not null)
                            return bin;
                    }

                    if (name.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("docs", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("readme", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("common", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var sub = FindVersionBinRecursive(dir, version, depth + 1, maxDepth);
                    if (sub != null)
                        return sub;
                }
            }
            catch
            {
                // нет доступа
            }
            return null;
        }

        // ------------------------------------------------------------------
        // Разбор каталогов
        // ------------------------------------------------------------------

        private static void ScanRoot(string root, List<PlatformVersionInfo> result, HashSet<string> seen)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                return;

            try
            {
                // 1. Каталоги версий внутри корня: /opt/1cv8/8.3.27.2214/bin/1cv8
                foreach (var dir in Directory.EnumerateDirectories(root))
                {
                    var name = Path.GetFileName(dir);
                    if (!LooksLikeVersion(name))
                        continue;
                    if (ResolveBinDirectory(dir) is null)
                        continue;
                    var arch = DetectArchitecture(dir);
                    if (!seen.Add(dir))
                        continue;
                    result.Add(new PlatformVersionInfo
                    {
                        Display = FormatVariant(name, arch),
                        Path = dir,
                        IsThinClient = IsThinClientDir(dir)
                    });
                }

                // 2. Сам корень — каталог версии (например ~/.1cv8/8.3.27.1).
                if (LooksLikeVersion(Path.GetFileName(root)))
                {
                    if (ResolveBinDirectory(root) is not null && seen.Add(root))
                    {
                        var arch = DetectArchitecture(root);
                        result.Add(new PlatformVersionInfo
                        {
                            Display = FormatVariant(Path.GetFileName(root), arch),
                            Path = root,
                            IsThinClient = IsThinClientDir(root)
                        });
                    }
                }

                // 3. /usr/bin/1cv8 — симлинк на конкретную версию.
                if (IsBinSymlinkRoot(root))
                    TryAddUsrBinSymlink(result, seen);
            }
            catch
            {
                // нет доступа
            }
        }

        private static bool IsBinSymlinkRoot(string root)
        {
            var trimmed = root.TrimEnd('/', '\\');
            return string.Equals(trimmed, "/usr/bin", StringComparison.Ordinal) ||
                   string.Equals(trimmed, "/usr/local/bin", StringComparison.Ordinal);
        }

        private static void TryAddUsrBinSymlink(List<PlatformVersionInfo> result, HashSet<string> seen)
        {
            try
            {
                var link = "/usr/bin/1cv8";
                if (!File.Exists(link))
                    return;
                var target = File.ResolveLinkTarget(link, returnFinalTarget: true)?.FullName;
                if (string.IsNullOrEmpty(target))
                    return;
                var binDir = Path.GetDirectoryName(target);
                if (string.IsNullOrEmpty(binDir))
                    return;

                // При раскладке <версия>/bin каталог версии на уровень выше,
                // при плоской раскладке каталог цели сам является каталогом версии.
                var versionDir = LooksLikeVersion(Path.GetFileName(binDir))
                    ? binDir
                    : Path.GetDirectoryName(binDir);
                if (string.IsNullOrEmpty(versionDir))
                    return;
                var name = Path.GetFileName(versionDir);
                if (!LooksLikeVersion(name))
                    return;
                var arch = DetectArchitecture(versionDir);
                if (seen.Add(versionDir))
                    result.Add(new PlatformVersionInfo
                    {
                        Display = FormatVariant(name, arch),
                        Path = versionDir,
                        IsThinClient = IsThinClientDir(versionDir)
                    });
            }
            catch
            {
                // ignore
            }
        }

        private static bool HasOneCBinary(string binDir)
        {
            try
            {
                return OneCBinaryNames.Any(n => File.Exists(Path.Combine(binDir, n)));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// True — в каталоге версии установлен ТОЛЬКО тонкий клиент: в bin есть 1cv8c,
        /// но нет полного 1cv8 (issue #33). Такая версия не даёт полных возможностей
        /// (конфигуратор, сервер и т.п.) и помечается в дереве выбора платформы.
        /// </summary>
        private static bool IsThinClientDir(string dir)
        {
            var bin = ResolveBinDirectory(dir);
            if (bin is null)
                return false;
            if (File.Exists(Path.Combine(bin, "1cv8")))
                return false;
            return File.Exists(Path.Combine(bin, "1cv8c"));
        }

        private static bool LooksLikeVersion(string name)
        {
            var parts = name.Split('.');
            return parts.Length >= 3 && parts.All(p => p.Length > 0 && int.TryParse(p, out _));
        }

        /// <summary>Определяет разрядность по каталогу и/или ELF-классу бинарника.</summary>
        private static string DetectArchitecture(string dir)
        {
            var lower = dir.Replace('\\', '/').ToLowerInvariant();

            if (lower.Contains("i386") || lower.Contains("i686") ||
                lower.Contains("x86-32") || lower.EndsWith("/x86", StringComparison.Ordinal) ||
                lower.Contains("1cv8.x86/") || lower.Contains("/x86/"))
                return "32";

            if (lower.Contains("x86_64") || lower.Contains("amd64"))
                return "64";

            var readelf = DetectArchViaReadelf(dir);
            if (readelf != null)
                return readelf;

            // По умолчанию (например, /opt/1cv8, ~/.1cv8) — 64-бит.
            return "64";
        }

        /// <summary>
        /// Каталог с исполняемыми файлами платформы для каталога версии.
        /// Раскладка отличается между дистрибутивами и версиями платформы:
        /// у одних бинарники лежат в подкаталоге bin, у других прямо в каталоге
        /// версии (например /opt/1cv8/x86_64/8.3.27.2214/1cv8). Проверяются оба
        /// варианта, возвращается тот, где бинарники действительно есть.
        /// </summary>
        private static string? ResolveBinDirectory(string? versionDir)
        {
            if (string.IsNullOrWhiteSpace(versionDir))
                return null;

            var bin = Path.Combine(versionDir, "bin");
            if (Directory.Exists(bin) && HasOneCBinary(bin))
                return bin;

            if (Directory.Exists(versionDir) && HasOneCBinary(versionDir))
                return versionDir;

            return null;
        }

        private static string? DetectArchViaReadelf(string dir)
        {
            var binDir = ResolveBinDirectory(dir);
            if (binDir is null)
                return null;
            // Установка может быть неполной: у версии с одним тонким клиентом
            // файла 1cv8 нет, разрядность читаем с любого доступного бинарника.
            var bin = OneCBinaryNames
                .Select(n => Path.Combine(binDir, n))
                .FirstOrDefault(File.Exists);
            if (bin is null)
                return null;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "readelf",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                psi.ArgumentList.Add("-h");
                psi.ArgumentList.Add(bin);
                using var p = Process.Start(psi);
                if (p is null)
                    return null;
                var text = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit(3000);
                if (text.Contains("ELF64", StringComparison.OrdinalIgnoreCase))
                    return "64";
                if (text.Contains("ELF32", StringComparison.OrdinalIgnoreCase))
                    return "32";
            }
            catch
            {
                // readelf может отсутствовать в системе
            }
            return null;
        }

        // ------------------------------------------------------------------
        // Форматирование и сравнение версий
        // ------------------------------------------------------------------

        public static string FormatVariant(string version, string architecture)
            => $"{version} ({architecture})";

        public static void ParseVariant(string variant, out string version, out string architecture)
        {
            version = variant;
            architecture = "64";
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
        /// <see cref="ParseVariant"/> по умолчанию возвращает разрядность даже без суффикса,
        /// поэтому выбор папки/частичной версии («8.3», «8.3.27») не должен подставлять x86,
        /// пока она не задана явно (issue #251). Разрешение разрядности остаётся за лаунчером.
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
        /// НЕ задана явно (варианты «8.3», «8.3.27»): ParseVariant по умолчанию подставляет
        /// разрядность, из-за чего такие версии «исчезали» из противоположного фильтра
        /// и ломали поиск узла (issue #304). Распознаются «(64)»/«(x64)» и «(32)»/«(x32)»/«(x86)».
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

        /// <summary>Линия платформы: «8.3.27.1688 (64)» → «8.3».</summary>
        public static string GetVersionLine(string variant)
        {
            ParseVariant(variant, out var version, out _);
            var parts = version.Split('.');
            return parts.Length >= 2
                ? string.Join(".", parts.Take(2))
                : (string.IsNullOrEmpty(version) ? "—" : version);
        }

        /// <summary>Группа сборки: «8.3.27.1688 (64)» → «8.3.27».</summary>
        public static string GetVersionBuildGroup(string variant)
        {
            ParseVariant(variant, out var version, out _);
            var parts = version.Split('.');
            return parts.Length >= 3
                ? string.Join(".", parts.Take(3))
                : GetVersionLine(variant);
        }

        /// <summary>Подпись разрядности в стиле стартера 1С: «x64» / «x32».</summary>
        public static string FormatArchitectureLabel(string? architecture)
        {
            if (string.IsNullOrWhiteSpace(architecture))
                return "";
            var a = architecture.Trim();
            if (a is "64" or "x64" or "X64")
                return "x64";
            if (a is "32" or "x32" or "X32" or "x86")
                return "x32";
            return a;
        }

        /// <summary>Числовое сравнение строк версий (без суффикса разрядности). >0 если a новее b.</summary>
        public static int CompareVersionStrings(string a, string b)
        {
            ParseVariant(a, out var va, out _);
            ParseVariant(b, out var vb, out _);
            var pa = ToIntArray(va);
            var pb = ToIntArray(vb);
            var len = Math.Max(pa.Length, pb.Length);
            for (var i = 0; i < len; i++)
            {
                var x = i < pa.Length ? pa[i] : 0;
                var y = i < pb.Length ? pb[i] : 0;
                if (x != y)
                    return x.CompareTo(y);
            }
            return 0;
        }

        private static int[] ToIntArray(string v)
        {
            var s = v.Split(new[] { '.', ' ', '(', ')' }, StringSplitOptions.RemoveEmptyEntries);
            var list = new List<int>();
            foreach (var p in s)
            {
                if (int.TryParse(p, out var n))
                    list.Add(n);
                else
                    break;
            }
            return list.ToArray();
        }

        /// <summary>
        /// Дерево выбора платформы: линия (8.3) → группа сборок (8.3.27) → версия.
        /// При активном фильтре разрядности скрываются только листья другой разрядности,
        /// а папки (линии и группы сборок) сохраняются, если в линии остался хотя бы один
        /// видимый вариант: выбор папки «8.5.1» не должен «перескакивать» на линию 8.5 при
        /// переключении фильтра, пока семейство 8.5 хоть как-то видимо (issue #304).
        /// </summary>
        public static List<PlatformVersionGroup> BuildGroupedTree(
            IEnumerable<PlatformVersionInfo> infos, string archFilter = "all")
        {
            var list = infos?.ToList() ?? new List<PlatformVersionInfo>();
            var roots = new List<PlatformVersionGroup>();

            var byLine = list
                .GroupBy(i => GetVersionLine(i.Display))
                .OrderByDescending(g => g.Key, new VersionDisplayComparer());

            foreach (var lineGroup in byLine)
            {
                // Линия остаётся в дереве, только если в ней есть хотя бы один лист,
                // проходящий фильтр разрядности. Иначе папка «8.5» пуста — показывать
                // нечего, fallback не нужен (issue #304).
                if (!lineGroup.Any(i => PassesArchFilter(i.Display, archFilter)))
                    continue;

                var lineNode = new PlatformVersionGroup { Name = lineGroup.Key, Kind = PlatformNodeKind.Line };

                var byBuild = lineGroup
                    .GroupBy(i => GetVersionBuildGroup(i.Display))
                    .OrderByDescending(g => g.Key, new VersionDisplayComparer());

                foreach (var buildGroup in byBuild)
                {
                    // Папка группы сборок («8.5.1») сохраняется даже без собственных
                    // видимых листьев: линия видима, а выбранная папка должна оставаться
                    // доступной при переключении разрядности (issue #304).
                    var buildNode = new PlatformVersionGroup { Name = buildGroup.Key, Kind = PlatformNodeKind.BuildGroup };

                    foreach (var info in buildGroup.OrderByDescending(i => i.Display, new VersionDisplayComparer()))
                    {
                        if (!PassesArchFilter(info.Display, archFilter))
                            continue;

                        // Имя листа с меткой разрядности «x64»/«x32», как в
                        // Windows-ветке (PlatformVersionService.cs:576): сырой
                        // Display даёт «(64)», а на экране должно быть «(x64)».
                        ParseVariant(info.Display, out var version, out var arch);
                        var archLabel = FormatArchitectureLabel(arch);
                        var leafName = string.IsNullOrEmpty(archLabel)
                            ? version
                            : $"{version} ({archLabel})";

                        var leaf = new PlatformVersionGroup
                        {
                            Name = leafName,
                            Variant = info.Display,
                            Path = info.Path,
                            Kind = ParseVariantKind(info.Display),
                            Versions = { info }
                        };
                        buildNode.Children.Add(leaf);
                    }
                    lineNode.Children.Add(buildNode);
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
        public static PlatformVersionGroup? FindBestNode(
            IEnumerable<PlatformVersionGroup> nodes, string currentVersion)
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
        private static PlatformVersionGroup? FindLine(
            IEnumerable<PlatformVersionGroup> nodes, string prefix)
            => nodes.FirstOrDefault(n =>
                !n.IsLeaf && string.Equals(n.Name, prefix, StringComparison.OrdinalIgnoreCase));

        /// <summary>Ищет группу сборок с указанным префиксом среди детей узла (линии).</summary>
        private static PlatformVersionGroup? FindBuildGroup(
            PlatformVersionGroup node, string prefix)
            => node.Children.FirstOrDefault(n =>
                !n.IsLeaf && string.Equals(n.Name, prefix, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Ищет узел группы сборок с указанным префиксом («8.3.27») среди переданных узлов.
        /// </summary>
        private static PlatformVersionGroup? FindBuildGroup(
            IEnumerable<PlatformVersionGroup> nodes, string prefix)
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
        public static PlatformVersionGroup? FindExactLeaf(
            IEnumerable<PlatformVersionGroup> nodes, string currentVersion)
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
        public static string BuildResultVariant(PlatformVersionGroup node, string archFilter)
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
        private static string? GetNodeArchSuffix(PlatformVersionGroup node, string archFilter)
        {
            if (archFilter == "all") return null;

            var kind = archFilter == "x64"
                ? PlatformNodeKind.LeafX64
                : PlatformNodeKind.LeafX32;
            if (!HasVisibleLeafOfKind(node, kind))
                return null;

            return archFilter == "x64" ? "64" : "32";
        }

        private static bool HasVisibleLeafOfKind(PlatformVersionGroup node, PlatformNodeKind kind)
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
        /// подставляет её по умолчанию, иначе выбранная версия «исчезала» из фильтра x64
        /// и выделение «скакало» (issue #304, #251).
        /// </summary>
        public static List<PlatformVersionInfo> FilterByArchitecture(
            IEnumerable<PlatformVersionInfo> infos, string filter)
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

        private static PlatformNodeKind ParseVariantKind(string variant)
        {
            ParseVariant(variant, out _, out var arch);
            return arch == "64" ? PlatformNodeKind.LeafX64
                : arch == "32" ? PlatformNodeKind.LeafX32
                : PlatformNodeKind.Leaf;
        }

        /// <summary>Сравнивает строки версий по старшинству чисел.</summary>
        private sealed class VersionDisplayComparer : IComparer<string>
        {
            public int Compare(string? x, string? y)
            {
                if (x is null && y is null) return 0;
                if (x is null) return -1;
                if (y is null) return 1;
                return CompareVersionStrings(x, y);
            }
        }
    }

    /// <summary>Адаптер интерфейса <see cref="IPlatformVersionService"/> для Linux.</summary>
    public sealed class PlatformVersionServiceAdapter : IPlatformVersionService
    {
        public List<string> FindInstalledVersions(IEnumerable<string>? additionalPaths = null)
            => PlatformVersionService.FindInstalledVersions(additionalPaths);
    }
}
#endif