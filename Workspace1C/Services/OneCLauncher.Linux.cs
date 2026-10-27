#if LINUX
using System.Collections.Concurrent;
using System.Diagnostics;
using Workspace1C.Models;

namespace Workspace1C.Services
{
    // ========================================================================
    // Типы запуска (общие для обеих платформ; OneCLaunchMode вынесен в общий
    // файл Services/OneCLaunchMode.cs, остальные типы определены здесь).
    // ========================================================================

    /// <summary>Тип клиента 1С:Предприятие.</summary>
    public enum OneCClientType
    {
        /// <summary>Тонкий клиент (управляемое приложение).</summary>
        Thin,
        /// <summary>Толстый клиент (обычное приложение).</summary>
        Thick
    }

    /// <summary>Режим форм приложения 1С:Предприятие.</summary>
    public enum OneCRunMode
    {
        /// <summary>Управляемые формы (/RunModeManagedApplication).</summary>
        Managed,
        /// <summary>Обычные формы (/RunModeOrdinaryApplication).</summary>
        Ordinary
    }

    /// <summary>Разрядность исполняемого файла платформы 1С.</summary>
    public enum OneCArchitecture
    {
        /// <summary>32-битная версия.</summary>
        x86,
        /// <summary>64-битная версия.</summary>
        x64
    }

    /// <summary>
    /// Сервис запуска платформы 1С:Предприятие на Linux.
    /// Запуск — через /opt/1cv8/<вер>/bin/1cv8 (или 1cv8c) через Process.Start
    /// без UseShellExecute. Командная строка 1С совместима с Windows.
    /// Разбит на partial-файлы по ответственности:
    /// <list type="bullet">
    /// <item>OneCLauncher.Linux.cs — ядро: типы, состояние, выбор разрядности;</item>
    /// <item>OneCLauncher.Linux.Process.cs — запуск процессов и пути к исполняемым файлам;</item>
    /// <item>OneCLauncher.Linux.Arguments.cs — аргументы подключения (остальные общие — в shared);</item>
    /// <item>OneCLauncher.Linux.DesignerBatch.cs — пакетные операции DESIGNER;</item>
    /// <item>OneCLauncher.Linux.Errors.cs — логгер из DI.</item>
    /// </list>
    /// </summary>
    public static partial class OneCLauncher
    {
        /// <summary>
        /// Режим глобальной «Разрядности по умолчанию» («Настройки → Платформы»):
        /// "X64" — всегда 64-бит, "X86" — всегда 32-бит, либо "Priority"
        /// («Использовать приоритет базы») — брать явную настройку разрядности базы.
        /// </summary>
        public static string DefaultArchitectureMode { get; set; } = "X64";

        /// <summary>
        /// Разрядность для «текущей сессии» (блок «Текущая сессия» в окне списка баз).
        /// Наивысший (первый) шаг приоритета выбора разрядности в <see cref="ResolveArchitecture"/>:
        /// если здесь задан конкретный режим (не Auto), он побеждает и суффикс версии,
        /// и глобальную настройку, и настройку базы. Auto — выбираем по шагам 2–4.
        /// Значение передаётся из <c>MainViewModel</c> (изменяется вместе с выбором в UI).
        /// </summary>
        public static SessionArchitectureMode SessionArchitecture { get; set; } = SessionArchitectureMode.Auto;

        private static readonly ConcurrentDictionary<string, Process> _activeBatchProcesses =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Возникает при запуске пакетной операции DESIGNER.</summary>
        public static event EventHandler<DesignerBatchInfo>? DesignerBatchStarted;

        /// <summary>Возникает при завершении пакетной операции DESIGNER.</summary>
        public static event EventHandler<DesignerBatchInfo>? DesignerBatchCompleted;

        /// <summary>Определяет режим форм по режиму запуска базы.</summary>
        public static OneCRunMode? GetRunModeFromLaunchMode(string? launchMode)
        {
            if (string.Equals(launchMode, "Толстый клиент (обычные формы)", StringComparison.OrdinalIgnoreCase))
                return OneCRunMode.Ordinary;
            if (string.Equals(launchMode, "Толстый клиент", StringComparison.OrdinalIgnoreCase))
                return OneCRunMode.Managed;
            if (string.Equals(launchMode, "Тонкий клиент", StringComparison.OrdinalIgnoreCase))
                return OneCRunMode.Managed;
            return null;
        }

        private static OneCArchitecture GetArchitecture(Infobase infobase)
            => ResolveArchitecture(infobase.Architecture, infobase.PlatformVersion);

        /// <summary>
        /// Выбор разрядности по правилам 1С:Предприятие.
        /// Порядок приоритетов (issue #146, комментарий 7OH; issue #73):
        /// 1. «Текущая сессия» — выбранное значение в группе «Текущая сессия»
        ///    (свойство <see cref="SessionArchitecture"/>, передаётся из MainViewModel).
        /// 2. Суффикс разрядности в выбранной версии платформы («8.3.27.1688 (64)»).
        /// 3. Явная настройка разрядности базы (вкладка «Разрядность»): «32»/«x86» → x86,
        ///    «64»/«x64» → x64. Явная разрядность базы перебивает глобальную настройку
        ///    «Разрядность по умолчанию» (issue #73).
        /// 4. Глобальная настройка «Разрядность по умолчанию»: X64 / X86 либо
        ///    режим «Использовать приоритет базы» (Priority). Применяется, когда
        ///    у базы разрядность не указана.
        /// 5. Приоритетные режимы базы 32-priority / 64-priority по стилю 1С.
        /// </summary>
        public static OneCArchitecture ResolveArchitecture(string? architectureSetting, string? platformVersion)
        {
            // 1. «Текущая сессия» — первый (наивысший) шаг приоритета (issue #146).
            //    Явный выбор разрядности в блоке «Текущая сессия» побеждает суффикс
            //    версии, глобальную настройку и настройку базы.
            if (SessionArchitecture == SessionArchitectureMode.X86)
                return OneCArchitecture.x86;
            if (SessionArchitecture == SessionArchitectureMode.X64)
                return OneCArchitecture.x64;

            // 2. Если в версии платформы явно указан суффикс разрядности («8.3.27.1688 (64)») —
            //    пользователь выбрал конкретную сборку. Это следующий по приоритету шаг
            //    после «текущей сессии» и перебивает глобальную настройку.
            PlatformVersionService.ParseVariant(platformVersion ?? string.Empty, out var cleanVersion, out var versionArch);
            var hasSuffix = !string.IsNullOrWhiteSpace(platformVersion)
                && platformVersion.Contains("(") && platformVersion.Contains(")");
            if (hasSuffix && !string.IsNullOrWhiteSpace(cleanVersion) && (versionArch == "32" || versionArch == "64"))
                return versionArch == "64" ? OneCArchitecture.x64 : OneCArchitecture.x86;

            // 3. Явная настройка разрядности базы (вкладка «Разрядность»).
            //    Явно указанная разрядность базы перебивает глобальную настройку
            //    «Разрядность по умолчанию» (issue #73).
            var mode = (architectureSetting ?? string.Empty).Trim().ToLowerInvariant();
            if (mode is "64" or "x64" or "x86-64" or "x86_64")
                return OneCArchitecture.x64;
            if (mode is "32" or "x86")
                return OneCArchitecture.x86;

            // 4. Глобальная настройка «Разрядность по умолчанию»
            //    (Настройки → Платформы → «Разрядность по умолчанию»).
            //    Применяется, когда у базы разрядность не указана.
            var defaultMode = string.IsNullOrWhiteSpace(DefaultArchitectureMode) ? "X64" : DefaultArchitectureMode.Trim();
            if (string.Equals(defaultMode, "X86", StringComparison.OrdinalIgnoreCase))
                return OneCArchitecture.x86;
            if (string.Equals(defaultMode, "X64", StringComparison.OrdinalIgnoreCase))
                return OneCArchitecture.x64;
            // defaultMode == "Priority" («Использовать приоритет базы») → шаг 5.

            // 5. Приоритетные режимы базы: сравниваем лучшие доступные версии 32 и 64.
            var prefer64 = mode is "64-priority" or "priority64" or "x86-64-priority";
            var v32 = FindBestVersionDir("32", cleanVersion);
            var v64 = FindBestVersionDir("64", cleanVersion);

            if (v32 is null && v64 is null)
                return prefer64 ? OneCArchitecture.x64 : OneCArchitecture.x86;
            if (v32 is null)
                return OneCArchitecture.x64;
            if (v64 is null)
                return OneCArchitecture.x86;

            var cmp = PlatformVersionService.CompareVersionStrings(v32, v64);
            if (cmp > 0)
                return OneCArchitecture.x86;
            if (cmp < 0)
                return OneCArchitecture.x64;
            return prefer64 ? OneCArchitecture.x64 : OneCArchitecture.x86;
        }

        private static string? FindBestVersionDir(string archKey, string preferredVersion)
        {
            var entries = PlatformVersionService.FindPlatformVersionDirs(archKey);
            string? best = null;
            foreach (var (version, _) in entries)
            {
                // Полная версия — только точные совпадения; частичная — по префиксу (issue #142).
                if (!string.IsNullOrWhiteSpace(preferredVersion) &&
                    !VersionMatches(preferredVersion, version))
                    continue;
                if (best is null || PlatformVersionService.CompareVersionStrings(version, best) > 0)
                    best = version;
            }
            return best;
        }

        /// <summary>
        /// Проверяет, соответствует ли фактическая версия запрошенной.
        /// Полная версия (4 сегмента) — точное совпадение; частичная («8.5», «8.3.27») —
        /// по числовому префиксу (issue #142).
        /// </summary>
        private static bool VersionMatches(string requested, string actual)
        {
            if (string.IsNullOrWhiteSpace(requested))
                return true;

            var reqParts = requested.Split('.');
            // Полная версия — только точное совпадение (как раньше).
            if (reqParts.Length >= 4)
                return string.Equals(actual, requested, StringComparison.OrdinalIgnoreCase);

            // Частичная версия — префиксное сопоставление сегментов.
            var actParts = actual.Split('.');
            if (actParts.Length < reqParts.Length)
                return false;
            for (var i = 0; i < reqParts.Length; i++)
            {
                if (!string.Equals(actParts[i].Trim(), reqParts[i].Trim(), StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }
    }

    /// <summary>Реализация <see cref="IOneCLauncher"/> для Linux.</summary>
    public sealed class OneCLauncherService : IOneCLauncher
    {
        public bool Launch(Infobase infobase, OneCLaunchMode mode, bool runAsAdmin = false)
            => OneCLauncher.Launch(infobase, mode, runAsAdmin);

        public bool Launch(Infobase infobase, OneCLaunchMode mode, OneCClientType? clientType, OneCArchitecture architecture, bool runAsAdmin = false)
            => OneCLauncher.Launch(infobase, mode, clientType, architecture, runAsAdmin);

        public bool Launch(Infobase infobase, OneCLaunchMode mode, OneCClientType? clientType, OneCRunMode? runMode, OneCArchitecture architecture, bool runAsAdmin = false)
            => OneCLauncher.Launch(infobase, mode, clientType, runMode, architecture, runAsAdmin);
    }
}
#endif
