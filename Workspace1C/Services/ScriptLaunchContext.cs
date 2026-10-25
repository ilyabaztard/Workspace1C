using System;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Контекст запуска платформы 1С:Предприятие для подстановок сценариев (issue #67):
/// полные строки запуска, исполняемый файл и версия платформы для текущей базы.
/// Платформенно-нейтральная модель — передаётся в <see cref="ScriptParameterResolver.BuildValueMap"/>
/// и отображается в токенах <c>%full_launch_enterprise%</c>, <c>%full_launch_config%</c>,
/// <c>%full_exe_name%</c>, <c>%platform_version%</c>.
/// </summary>
/// <param name="ExePath">Полный путь к исполняемому файлу платформы (1cv8.exe / 1cv8).</param>
/// <param name="PlatformVersion">Полная версия платформы (например 8.5.4.1878), без суффикса разрядности.</param>
/// <param name="EnterpriseCommandLine">Полная строка запуска, которую стартер подставил бы в режиме «1С:Предприятие».</param>
/// <param name="ConfiguratorCommandLine">Полная строка запуска, которую стартер подставил бы в режиме «Конфигуратор».</param>
public sealed record ScriptLaunchContext(
    string ExePath,
    string PlatformVersion,
    string EnterpriseCommandLine,
    string ConfiguratorCommandLine);

/// <summary>
/// Собирает <see cref="ScriptLaunchContext"/> для информационной базы по установленной
/// платформе: резолвит каталог <c>bin</c> (точная версия базы → новейшая установленная),
/// находит исполняемый файл платформы и собирает полные строки запуска Enterprise/Configurator
/// через <see cref="OneCLauncher.BuildArguments"/> (та же логика, что у реального стартера —
/// issue #67). Любые ошибки платформенного опроса не роняют построение карты подстановок —
/// возвращается null (токены остаются пустыми).
/// </summary>
public static class ScriptLaunchContextResolver
{
    /// <summary>
    /// Переопределение резолвера для юнит-тестов (по образцу других сервисов с файловым
    /// I/O: PlatformVersionService/MenuCloseTrace). Намеренно internal.
    /// </summary>
    internal static Func<Infobase?, ScriptLaunchContext?>? ResolverOverride { get; set; }

    /// <summary>Резолвит контекст запуска платформы для базы; null — платформа не найдена.</summary>
    public static ScriptLaunchContext? Resolve(Infobase? infobase)
    {
        if (ResolverOverride is not null)
            return ResolverOverride(infobase);
        return ResolveCore(infobase);
    }

    private static ScriptLaunchContext? ResolveCore(Infobase? infobase)
    {
        if (infobase is null)
            return null;

        try
        {
            var binDir = OneCPlatformLocator.ResolveBinDirectory(infobase);
            if (string.IsNullOrWhiteSpace(binDir))
                return null;

            var arch = OneCLauncher.ResolveArchitecture(infobase.Architecture, infobase.PlatformVersion);
            var archKey = arch == OneCArchitecture.x64 ? "64" : "32";

            // Версия платформы: каталог bin фактически используемой платформы; запасной
            // вариант — новейшая из установленных нужной разрядности.
            var version = ResolveVersionForBinDir(binDir!, archKey) ?? string.Empty;

            var exe = OneCPlatformLocator.FindInBinDir(binDir!, "1cv8");
            if (string.IsNullOrEmpty(exe))
                return null;

            var enterpriseArgs = OneCLauncher.BuildArguments(infobase, OneCLaunchMode.Enterprise, null, null);
            var configuratorArgs = OneCLauncher.BuildArguments(infobase, OneCLaunchMode.Configurator, null, null);

            return new ScriptLaunchContext(
                ExePath: exe!,
                PlatformVersion: version,
                EnterpriseCommandLine: BuildFullCommand(exe!, enterpriseArgs),
                ConfiguratorCommandLine: BuildFullCommand(exe!, configuratorArgs));
        }
        catch
        {
            // Платформа недоступна/ошибка опроса — токены остаются пустыми (не ломаем сценарий).
            return null;
        }
    }

    /// <summary>Версия установленной платформы, чей каталог bin совпадает с найденным
    /// (PathComparison по регистру ОС); при отсутствии совпадения — новейшая из установленных.</summary>
    private static string? ResolveVersionForBinDir(string binDir, string archKey)
    {
        string? best = null;
        var osComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        foreach (var (version, dir) in PlatformVersionService.FindPlatformVersionDirs(archKey))
        {
            if (string.Equals(dir, binDir, osComparison))
                return version;
            if (best is null || CompareVersions(version, best) > 0)
                best = version;
        }
        return best;
    }

    /// <summary>Полная строка запуска: исполняемый файл (в кавычках при пробелах) + аргументы.</summary>
    private static string BuildFullCommand(string exe, string args)
    {
        var exePart = exe.IndexOf(' ') < 0 ? exe : "\"" + exe + "\"";
        return string.IsNullOrWhiteSpace(args) ? exePart : exePart + " " + args.Trim();
    }

    /// <summary>Сравнение номеров версий 1С по числовым сегментам (как PlatformVersionService).</summary>
    private static int CompareVersions(string a, string b)
    {
        static int[] Parts(string v)
        {
            var list = new System.Collections.Generic.List<int>();
            foreach (var part in v.Split(new[] { '.', ' ', '(' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(part, out var number))
                    list.Add(number);
            }
            return list.ToArray();
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
}