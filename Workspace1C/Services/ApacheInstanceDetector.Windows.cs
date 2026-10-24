#if WINDOWS
using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;
using Workspace1C.Models;

namespace Workspace1C.Services
{
    /// <summary>
    /// Windows-часть <see cref="ApacheInstanceDetector"/> (issue #63): перечисление
    /// служб httpd (имя/DisplayName/Description) через реестр <c>SYSTEM\CurrentControlSet\Services</c>,
    /// из <c>ImagePath</c> извлекаются ключи <c>-d ServerRoot</c> и <c>-f conf</c>
    /// (<see cref="ApacheInstanceDetector.ParseWindowsServiceImagePath"/>), конфиг читается
    /// и разбираются include-директивы. Права администратора не требуются (чтение реестра).
    /// </summary>
    public static partial class ApacheInstanceDetector
    {
        private const string ServicesRegistryPath = @"SYSTEM\CurrentControlSet\Services";

        private static IReadOnlyList<ApacheInstance> DetectWindows()
        {
            var result = new List<ApacheInstance>();
            try
            {
                using var servicesKey = Registry.LocalMachine.OpenSubKey(ServicesRegistryPath);
                if (servicesKey is null)
                    return result;

                foreach (var serviceName in servicesKey.GetSubKeyNames())
                {
                    try
                    {
                        using var key = servicesKey.OpenSubKey(serviceName);
                        if (key is null)
                            continue;

                        var displayName = key.GetValue("DisplayName") as string ?? string.Empty;
                        var description = key.GetValue("Description") as string ?? string.Empty;
                        if (!LooksLikeApache(serviceName) && !LooksLikeApache(displayName) && !LooksLikeApache(description))
                            continue;

                        var imagePath = key.GetValue("ImagePath") as string;
                        var instance = ParseWindowsServiceImagePath(serviceName, displayName, description, imagePath);
                        if (instance is null)
                            continue;

                        LoadConfIncludes(instance);
                        result.Add(instance);
                    }
                    catch
                    {
                        // Служба недоступна (нет прав/битая запись) — пропускаем.
                    }
                }
            }
            catch
            {
                // Реестр недоступен — пустой список, окно использует fallback.
            }

            // Хотя бы один инстанс с реальным конфигом — рекурсивно не требуется: fallback
            // выполняется в WebPublishViewModel, если список пуст.
            return result;
        }

        private static void LoadConfIncludes(ApacheInstance instance)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(instance.ConfPath) || !File.Exists(instance.ConfPath))
                    return;
                var text = File.ReadAllText(instance.ConfPath);
                var confDirectory = Path.GetDirectoryName(instance.ConfPath) ?? string.Empty;
                PopulateIncludes(instance, text, confDirectory);
            }
            catch
            {
                // Некорректный конфиг не важен для списка инстансов.
            }
        }
    }
}
#endif