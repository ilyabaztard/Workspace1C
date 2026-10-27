using System.IO;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Сбор дневного снимка размеров для информационной базы (п.7, выпуск 1.0.0.89):
/// размер ИБ (передаётся из кеша замеров <see cref="FileSizeCacheEntry"/> либо считается
/// здесь), объём каталога копий базы (основной каталог назначения) и свободное место
/// на диске этого каталога. Тяжёлые операции выполняет вызывающий поток (обычно
/// фоновый <c>Task.Run</c>): сканирование каталога копий может быть затратным.
/// </summary>
public static class SizeSnapshotCollector
{
    /// <summary>
    /// Основной каталог копий базы: первый непустой каталог назначения первого
    /// сценария резервирования, иначе первый из <see cref="AppSettings.BackupTargetDirectories"/>.
    /// Пусто/ничего не настроено — null.
    /// </summary>
    public static string? ResolveCopiesDirectory(
        Infobase ib, IReadOnlyList<BackupScenario> scenarios, AppSettings? settings)
    {
        if (scenarios is not null)
        {
            foreach (var scenario in scenarios)
            {
                if (scenario is null)
                    continue;
                var first = scenario.TargetDirectories?
                    .FirstOrDefault(d => !string.IsNullOrWhiteSpace(d))?.Trim();
                if (!string.IsNullOrWhiteSpace(first))
                    return first;
            }
        }
        var extra = settings?.BackupTargetDirectories;
        if (extra is not null)
        {
            var first = extra.FirstOrDefault(d => !string.IsNullOrWhiteSpace(d))?.Trim();
            if (!string.IsNullOrWhiteSpace(first))
                return first;
        }
        return null;
    }

    /// <summary>Общий размер каталога (рекурсивно); ошибки доступа и занятые файлы пропускаются.</summary>
    public static long? MeasureDirectorySize(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return null;
        long total = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(file).Length; }
                catch { /* занятый файл / отказ доступа — пропускаем */ }
            }
            return total;
        }
        catch
        {
            return total > 0 ? total : null;
        }
    }

    /// <summary>
    /// Свободное место на диске каталога (резолвер инжектируется — тесты). Ошибки —
    /// тихая деградация в null.
    /// </summary>
    public static long? MeasureDriveFree(string? directory, Func<string, DiskFreeInfo?> resolver)
    {
        if (string.IsNullOrWhiteSpace(directory) || resolver is null)
            return null;
        try
        {
            return DiskFreeSpaceHelper.TryGetInfo(directory, resolver)?.FreeBytes;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Имя диска каталога копий (корень пути).</summary>
    public static string? ResolveDriveName(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return null;
        return DiskFreeSpaceHelper.ResolveDriveName(directory);
    }

    /// <summary>
    /// Формирует дневной снимок базы. <paramref name="sizeBytes"/> — уже вычисленный размер
    /// ИБ (null — посчитать <see cref="InfobaseMaintenanceService.CalculateFileBaseSize"/>),
    /// <paramref name="scenarios"/> — сценарии резервирования (LoadAll), <paramref name="settings"/>
    /// — настройки приложения (для BackupTargetDirectories), <paramref name="driveResolver"/> —
    /// резолвер свободного места (по умолчанию — реальные диски).
    /// </summary>
    public static SizeSnapshotEntry Build(
        Infobase ib,
        long? sizeBytes,
        DateTime nowUtc,
        IReadOnlyList<BackupScenario>? scenarios = null,
        AppSettings? settings = null,
        Func<string, DiskFreeInfo?>? driveResolver = null)
    {
        var entry = new SizeSnapshotEntry
        {
            DateUtc = SizeHistoryStore.ToDayUtc(nowUtc),
            InfobaseId = ib?.Id ?? string.Empty,
            InfobaseName = ib?.Name?.Trim() ?? string.Empty,
            SizeBytes = sizeBytes ?? (ib is null ? null : InfobaseMaintenanceService.CalculateFileBaseSize(ib))
        };

        if (ib is not null && ib.Connection.Type == ConnectionType.File)
        {
            var copiesDir = ResolveCopiesDirectory(ib, scenarios ?? Array.Empty<BackupScenario>(), settings);
            if (!string.IsNullOrWhiteSpace(copiesDir))
            {
                entry.CopiesBytes = MeasureDirectorySize(copiesDir);
                entry.CopiesDrive = ResolveDriveName(copiesDir);
                entry.CopiesDiskFreeBytes = MeasureDriveFree(
                    copiesDir, driveResolver ?? DiskFreeSpaceHelper.DefaultDriveResolver);
            }
        }
        return entry;
    }
}
