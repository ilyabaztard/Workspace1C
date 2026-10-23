#if LINUX
using Workspace1C.Models;

namespace Workspace1C.Services
{
    /// <summary>
    /// Поиск исполняемого файла конфигуратора для произвольных пакетных команд
    /// (выпуск 1.0.0.69, <see cref="DesignerCommandRunner"/>; Linux): тот же порядок,
    /// что и в RunDesignerBatch — сначала разрядность базы, затем противоположная.
    /// </summary>
    public static partial class OneCLauncher
    {
        public static string? ResolveConfiguratorExecutable(Infobase infobase)
        {
            var arch = ResolveArchitecture(infobase.Architecture, infobase.PlatformVersion);
            var exePath = FindExecutable(infobase.PlatformVersion, arch, null, OneCLaunchMode.Configurator);
            if (string.IsNullOrEmpty(exePath))
            {
                var otherArch = arch == OneCArchitecture.x64 ? OneCArchitecture.x86 : OneCArchitecture.x64;
                exePath = FindExecutable(infobase.PlatformVersion, otherArch, null, OneCLaunchMode.Configurator);
            }
            return string.IsNullOrEmpty(exePath) ? null : exePath;
        }
    }
}
#endif
