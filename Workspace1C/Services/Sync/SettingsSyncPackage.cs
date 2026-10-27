using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Workspace1C.Services.Sync;

/// <summary>
/// Единый zip-пакет синхронизируемых файлов (п.9, 1.0.0.91): используется для
/// резервного снимка настроек и как транспорт упаковки в юнит-тестах. Внутри архива
/// пути хранятся относительными с прямыми слэшами (например «scripts/scenarios/
/// Calc.id.script.json»). Распаковка защищена от «zip-slip»: ни один путь не может
/// выйти за пределы целевого каталога.
/// </summary>
public static class SettingsSyncPackage
{
    /// <summary>Метаданные пакета (без секретов): версия приложения, машина, дата.</summary>
    public sealed class PackageMeta
    {
        public string AppVersion { get; set; } = string.Empty;
        public string Machine { get; set; } = string.Empty;
        public System.DateTime CreatedUtc { get; set; }
        public int FileCount { get; set; }
    }

    private static readonly JsonSerializerOptions MetaOptions = new() { WriteIndented = true };

    /// <summary>
    /// Безопасен ли относительный путь внутри пакета: не абсолютный, без «..», без
    /// пустых/корневых сегментов. Используется и при проверке шаблонов IncludePatterns.
    /// </summary>
    public static bool IsSafeRelativePath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return false;
        var normalized = relativePath.Replace('\\', '/');
        if (normalized.StartsWith("/", StringComparison.Ordinal) ||
            normalized.StartsWith("//", StringComparison.Ordinal))
            return false;

        var segments = normalized.Split('/');
        if (segments.Any(string.IsNullOrWhiteSpace))
            return false;
        foreach (var segment in segments)
        {
            if (segment == "." || segment == "..")
                return false;
            if (segment.IndexOf(':') >= 0 || segment.IndexOf('\0') >= 0)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Создаёт zip-архив из набора файлов. В архив попадают только безопасные относительные
    /// пути; каждая запись несёт последнее изменение исходного файла (LastWriteTime).
    /// </summary>
    public static bool CreatePackage(string archivePath, IEnumerable<KeyValuePair<string, string>> files)
    {
        var entries = (files ?? Enumerable.Empty<KeyValuePair<string, string>>())
            .Where(f => IsSafeRelativePath(f.Key))
            .Where(f => File.Exists(f.Value))
            .ToList();
        if (entries.Count == 0)
            return false;

        var dir = Path.GetDirectoryName(archivePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        try
        {
            using var zip = ZipFile.Open(archivePath, ZipArchiveMode.Create);
            foreach (var (relative, fullPath) in entries)
            {
                var entry = zip.CreateEntry(relative.Replace('\\', '/'), CompressionLevel.Fastest);
                entry.LastWriteTime = File.GetLastWriteTime(fullPath);
                using var src = File.OpenRead(fullPath);
                using var dst = entry.Open();
                src.CopyTo(dst);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Распаковывает архив в целевой каталог с проверкой путей (zip-slip защита):
    /// запись, выходящая за пределы target, пропускается. Возвращает число распакованных файлов.
    /// </summary>
    public static int ExtractPackage(string archivePath, string targetDirectory)
    {
        if (!File.Exists(archivePath))
            return 0;

        var root = Path.GetFullPath(targetDirectory);
        var extracted = 0;
        try
        {
            using var zip = ZipFile.OpenRead(archivePath);
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
                    continue;
                var relative = entry.FullName.Replace('\\', '/');
                var segments = relative.Split('/');
                if (segments.Any(s => s == "..") || relative.StartsWith("/", StringComparison.Ordinal))
                    continue;

                var destination = Path.GetFullPath(Path.Combine(root, Path.Combine(segments)));
                if (!destination.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                    !string.Equals(destination, root, StringComparison.OrdinalIgnoreCase))
                    continue;

                var fileDir = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(fileDir))
                    Directory.CreateDirectory(fileDir);

                entry.ExtractToFile(destination, overwrite: true);
                try
                {
                    if (entry.LastWriteTime != default)
                        File.SetLastWriteTimeUtc(destination, entry.LastWriteTime.UtcDateTime);
                }
                catch
                {
                    // Редкая ошибка установки даты не отменяет распаковку файла.
                }
                extracted++;
            }
            return extracted;
        }
        catch
        {
            return extracted;
        }
    }

    /// <summary>Сериализует метаданные пакета (JSON, без секретов).</summary>
    public static string SerializeMeta(PackageMeta meta) => JsonSerializer.Serialize(meta, MetaOptions);

    /// <summary>Читает метаданные пакета; null при ошибке/отсутствии.</summary>
    public static PackageMeta? DeserializeMeta(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<PackageMeta>(json, MetaOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Асинхронная обёртка распаковки для единообразия API.</summary>
    public static Task<int> ExtractPackageAsync(string archivePath, string targetDirectory, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(ExtractPackage(archivePath, targetDirectory));
    }
}