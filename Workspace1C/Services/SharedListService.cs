using System.IO;
using System.Net.Http;
using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>Источник общего списка баз отдела.</summary>
public sealed class SharedListSource
{
    /// <summary>Имя списка — оно же имя корневой группы «Общие: …».</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Путь к файлу ibases.v8i (локальный или сетевой) либо http(s)-адрес.</summary>
    public string Location { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;
}

/// <summary>Настройки общих списков (<c>shared_lists.json</c>).</summary>
public sealed class SharedListSettings
{
    public List<SharedListSource> Sources { get; set; } = new();

    /// <summary>Интервал автоматической синхронизации, минут (0 — только при запуске и вручную).</summary>
    public int IntervalMinutes { get; set; } = 60;
}

/// <summary>Итог слияния общего списка.</summary>
public sealed class SharedListMergeResult
{
    public int Added { get; set; }
    public int Updated { get; set; }
    public int Removed { get; set; }
    public int Skipped { get; set; }

    public bool Changed => Added + Updated + Removed > 0;
}

/// <summary>
/// Общий список баз отдела (выпуск 1.0.0.69): подписка на центральный файл ibases.v8i
/// (сетевая папка или http-адрес). Базы источника попадают в корневую группу
/// с именем списка с сохранением подгрупп, помечаются <see cref="Infobase.SharedSource"/>
/// и обновляются при синхронизации: новые добавляются, изменённые обновляются,
/// исчезнувшие из файла — удаляются. Личные базы пользователя не затрагиваются;
/// база с тем же ID, что у личной, пропускается. Слияние — чистая функция (тесты).
/// </summary>
public static class SharedListService
{
    public const string FileName = "shared_lists.json";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };

    public static SharedListSettings Load() => FeatureDataStore.Load<SharedListSettings>(FileName);

    public static void Save(SharedListSettings settings) => FeatureDataStore.Save(FileName, settings);

    /// <summary>
    /// Имя корневой группы источника — имя списка (не зависит от языка интерфейса,
    /// иначе смена языка переносила бы базы в новую группу).
    /// </summary>
    public static string RootGroupName(string sourceName) => sourceName.Trim();

    /// <summary>Читает базы источника (http — через временный файл).</summary>
    public static async Task<List<Infobase>> ReadSourceAsync(SharedListSource source, CancellationToken ct = default)
    {
        var location = source.Location?.Trim() ?? string.Empty;
        if (location.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || location.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var temp = Path.Combine(Path.GetTempPath(), $"shared_{Guid.NewGuid():N}.v8i");
            try
            {
                var bytes = await Http.GetByteArrayAsync(location, ct).ConfigureAwait(false);
                await File.WriteAllBytesAsync(temp, bytes, ct).ConfigureAwait(false);
                return IbasesV8iImporter.ReadInfobases(temp);
            }
            finally
            {
                try { File.Delete(temp); } catch { /* временный файл */ }
            }
        }

        if (!File.Exists(location))
            throw new FileNotFoundException(string.Format(LocalizationManager.T("Shared.NotFound"), location));
        return IbasesV8iImporter.ReadInfobases(location);
    }

    /// <summary>Гарантирует цепочку групп по полному пути (« / »), возвращает путь.</summary>
    public static string EnsureGroupPath(IList<Group> groups, string fullPath)
    {
        var segments = fullPath.Split(new[] { GroupHierarchyHelper.PathSeparator }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();
        var parentId = string.Empty;
        var path = string.Empty;
        foreach (var segment in segments)
        {
            path = path.Length == 0 ? segment : path + GroupHierarchyHelper.PathSeparator + segment;
            var existing = GroupHierarchyHelper.FindByFullPath(path, groups);
            if (existing is null)
            {
                existing = new Group
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = segment,
                    ParentId = parentId,
                    Color = "#0EA5E9"
                };
                groups.Add(existing);
            }
            parentId = existing.Id;
        }
        return path;
    }

    private static string Key(Infobase ib) =>
        (ib.Name ?? string.Empty).Trim().ToUpperInvariant() + "|" +
        (ib.Connection?.ToConnectionString() ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>
    /// Сливает базы источника <paramref name="sourceName"/> в список приложения.
    /// </summary>
    public static SharedListMergeResult Merge(IList<Infobase> infobases, IList<Group> groups, string sourceName,
        IReadOnlyList<Infobase> incoming)
    {
        var result = new SharedListMergeResult();
        var name = sourceName.Trim();
        var root = RootGroupName(name);
        var ours = infobases.Where(b => string.Equals(b.SharedSource, name, StringComparison.Ordinal)).ToList();
        var personalIds = new HashSet<string>(
            infobases.Where(b => string.IsNullOrEmpty(b.SharedSource) && !string.IsNullOrEmpty(b.Id)).Select(b => b.Id),
            StringComparer.OrdinalIgnoreCase);
        var matched = new HashSet<Infobase>();

        foreach (var item in incoming)
        {
            if (!string.IsNullOrEmpty(item.Id) && personalIds.Contains(item.Id))
            {
                result.Skipped++;
                continue;
            }

            var groupPath = string.IsNullOrWhiteSpace(item.Group)
                ? root
                : root + GroupHierarchyHelper.PathSeparator + item.Group.Trim();
            groupPath = EnsureGroupPath(groups, groupPath);

            var existing = ours.FirstOrDefault(b => !matched.Contains(b)
                && ((!string.IsNullOrEmpty(item.Id) && string.Equals(b.Id, item.Id, StringComparison.OrdinalIgnoreCase))
                    || Key(b) == Key(item)));
            if (existing is null)
            {
                item.SharedSource = name;
                item.Group = groupPath;
                if (string.IsNullOrEmpty(item.Id))
                    item.Id = Guid.NewGuid().ToString();
                infobases.Add(item);
                matched.Add(item);
                result.Added++;
                continue;
            }

            matched.Add(existing);
            var changed = false;
            if (Key(existing) != Key(item))
            {
                existing.Name = item.Name;
                existing.Connection = item.Connection;
                changed = true;
            }
            if (!string.Equals(existing.Group, groupPath, StringComparison.Ordinal))
            {
                existing.Group = groupPath;
                changed = true;
            }
            if (!string.IsNullOrWhiteSpace(item.PlatformVersion)
                && !string.Equals(existing.PlatformVersion, item.PlatformVersion, StringComparison.Ordinal))
            {
                existing.PlatformVersion = item.PlatformVersion;
                changed = true;
            }
            if (changed)
                result.Updated++;
        }

        foreach (var stale in ours.Where(b => !matched.Contains(b)).ToList())
        {
            infobases.Remove(stale);
            result.Removed++;
        }
        return result;
    }

    /// <summary>Удаляет все базы источника (отписка).</summary>
    public static int RemoveSource(IList<Infobase> infobases, string sourceName)
    {
        var stale = infobases.Where(b => string.Equals(b.SharedSource, sourceName.Trim(), StringComparison.Ordinal)).ToList();
        foreach (var b in stale)
            infobases.Remove(b);
        return stale.Count;
    }

    /// <summary>
    /// Синхронизирует все включённые источники в копиях списков. Возвращает итоговые
    /// списки, суммарный результат и тексты ошибок по источникам.
    /// </summary>
    public static async Task<(List<Infobase> Infobases, List<Group> Groups, SharedListMergeResult Result, List<string> Errors)>
        SyncAllAsync(IEnumerable<Infobase> current, IEnumerable<Group> currentGroups, SharedListSettings settings,
            CancellationToken ct = default)
    {
        var infobases = current.ToList();
        var groups = currentGroups.ToList();
        var total = new SharedListMergeResult();
        var errors = new List<string>();
        foreach (var source in settings.Sources.Where(s => s.Enabled && !string.IsNullOrWhiteSpace(s.Name)))
        {
            try
            {
                var incoming = await ReadSourceAsync(source, ct).ConfigureAwait(false);
                var r = Merge(infobases, groups, source.Name, incoming);
                total.Added += r.Added;
                total.Updated += r.Updated;
                total.Removed += r.Removed;
                total.Skipped += r.Skipped;
            }
            catch (Exception ex)
            {
                errors.Add($"{source.Name}: {ex.Message}");
            }
        }
        return (infobases, groups, total, errors);
    }
}
