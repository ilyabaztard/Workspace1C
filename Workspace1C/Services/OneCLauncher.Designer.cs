#if WINDOWS
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Поиск исполняемого файла конфигуратора для произвольных пакетных команд
/// (выпуск 1.0.0.69, <see cref="DesignerCommandRunner"/>): тот же порядок, что и в
/// <c>RunDesignerBatch</c> —
/// сначала разрядность базы, затем противоположная; 1CEStart не годится.
/// </summary>
public static partial class OneCLauncher
{
    public static string? ResolveConfiguratorExecutable(Infobase infobase)
    {
        var arch = ResolveArchitecture(infobase.Architecture, infobase.PlatformVersion);
        var exePath = FindExecutable(infobase.PlatformVersion, arch, null, OneCLaunchMode.Configurator);
        if (string.IsNullOrEmpty(exePath) ||
            exePath.EndsWith("1CEStart.exe", StringComparison.OrdinalIgnoreCase))
        {
            var otherArch = arch == OneCArchitecture.x64 ? OneCArchitecture.x86 : OneCArchitecture.x64;
            var fallback = FindExecutable(infobase.PlatformVersion, otherArch, null, OneCLaunchMode.Configurator);
            if (!string.IsNullOrEmpty(fallback) &&
                !fallback.EndsWith("1CEStart.exe", StringComparison.OrdinalIgnoreCase))
                exePath = fallback;
        }

        if (string.IsNullOrEmpty(exePath) ||
            exePath.EndsWith("1CEStart.exe", StringComparison.OrdinalIgnoreCase))
            return null;
        return exePath;
    }
}
#endif
