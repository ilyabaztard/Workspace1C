using System.Globalization;
using System.IO;
using System.Text.Json;
using Workspace1C.Models;

namespace Workspace1C.Services.Sync;

/// <summary>Операция синхронизации файла (п.9, 1.0.0.91): направление изменения.</summary>
public enum SyncOperation
{
    /// <summary>Загрузить локальную версию в хранилище (локальная новее / её нет на удалённой стороне).</summary>
    Upload,

    /// <summary>Скачать удалённую версию локально (удалённая новее / её нет локально).</summary>
    Download,

    /// <summary>Пропустить: версии идентичны (время/размер совпадают).</summary>
    Skip
}

/// <summary>Состояние файла на одной из сторон (локально или на удалённом хранилище).</summary>
public sealed record SyncFileState(string RelativePath, DateTime? LastWriteUtc, long? Size)
{
    public bool Exists => LastWriteUtc.HasValue;
}

/// <summary>
/// Строка плана синхронизации: что делать с конкретным файлом и нужно ли сохранить
/// проигравшую версию как <c>*.conflict-<дата></c>.
/// </summary>
public sealed record SyncPlanEntry(
    string RelativePath,
    SyncOperation Operation,
    DateTime? LocalLastWriteUtc,
    DateTime? RemoteLastWriteUtc)
{
    /// <summary>Обе версии существуют — проигравшая сохраняется рядом как резерв.</summary>
    public bool NeedsConflictBackup => LocalLastWriteUtc.HasValue && RemoteLastWriteUtc.HasValue;
}

/// <summary>Итог синхронизации: изменённые файлы, конфликты, ошибки (без секретов).</summary>
public sealed class SettingsSyncResult
{
    public bool Success { get; set; }
    public List<string> ChangedFiles { get; } = new();
    public List<string> Conflicts { get; } = new();
    public List<string> Errors { get; } = new();
    public string Message { get; set; } = string.Empty;
    public DateTime? LastSyncUtc { get; set; }

    public void AddError(string key, params object[] args)
    {
        var text = Workspace1C.Localization.LocalizationManager.T(key);
        Errors.Add(args.Length == 0 ? text : string.Format(CultureInfo.InvariantCulture, text, args));
    }
}

/// <summary>
/// Построение плана синхронизации по состояниям сторон (чистая функция, покрыта тестами):
/// last-write-wins. Условия:
/// <list type="bullet">
///   <item>только локально — Upload;</item>
///   <item>только удалённо — Download;</item>
///   <item>обе стороны: новее побеждает (Upload для локальной, Download для удалённой),
///   версии с одинаковой датой считаются идентичными — Skip;</item>
///   <item>порядок списка сохраняется (детерминизм плана).</item>
/// </list>
/// Точность сравнения — 2 секунды (разница квантования дат в WebDAV/файловых системах).
/// </summary>
public static class SettingsSyncPlan
{
    /// <summary>Максимальная разница дат (сек), считающаяся «одинаковой» (квантование WebDAV до секунды).</summary>
    public const double EqualThresholdSeconds = 2.0;

    public static List<SyncPlanEntry> Build(IReadOnlyList<SyncFileState> local, IReadOnlyList<SyncFileState> remote)
    {
        var remoteByPath = new Dictionary<string, SyncFileState>(StringComparer.Ordinal);
        foreach (var state in remote)
            remoteByPath[state.RelativePath] = state;

        var plan = new List<SyncPlanEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var localState in local)
        {
            seen.Add(localState.RelativePath);
            remoteByPath.TryGetValue(localState.RelativePath, out var remoteState);
            plan.Add(BuildPlanEntry(localState, remoteState));
        }

        // Удалённые файлы, которых нет локально (только remote) — Download.
        foreach (var remoteState in remote)
        {
            if (seen.Contains(remoteState.RelativePath))
                continue;
            plan.Add(BuildPlanEntry(null, remoteState));
        }

        return plan;
    }

    /// <summary>Планирует один файл по паре состояний (null — файла нет на стороне).</summary>
    public static SyncPlanEntry BuildPlanEntry(SyncFileState? local, SyncFileState? remote)
    {
        var rel = (local ?? remote)?.RelativePath ?? string.Empty;
        var localTime = local?.LastWriteUtc;
        var remoteTime = remote?.LastWriteUtc;

        if (!localTime.HasValue && !remoteTime.HasValue)
            return new SyncPlanEntry(rel, SyncOperation.Skip, localTime, remoteTime);
        if (!localTime.HasValue)
            return new SyncPlanEntry(rel, SyncOperation.Download, localTime, remoteTime);
        if (!remoteTime.HasValue)
            return new SyncPlanEntry(rel, SyncOperation.Upload, localTime, remoteTime);

        var diff = localTime.Value - remoteTime.Value;
        if (Math.Abs(diff.TotalSeconds) <= EqualThresholdSeconds)
            return new SyncPlanEntry(rel, SyncOperation.Skip, localTime, remoteTime);

        return localTime.Value > remoteTime.Value
            ? new SyncPlanEntry(rel, SyncOperation.Upload, localTime, remoteTime)
            : new SyncPlanEntry(rel, SyncOperation.Download, localTime, remoteTime);
    }

    /// <summary>Путь к резервной копии проигравшей версии: «sanitized…<сторона>.conflict-дата».</summary>
    public static string ConflictBackupName(string relativePath, string side, DateTime when)
    {
        var sanitized = new string(relativePath.Select(c =>
            (char.IsLetterOrDigit(c) || c is '_' or '-' or '.') ? c : '_').ToArray());
        return $"{sanitized}.{side}.conflict-{when:yyyyMMdd-HHmmss}";
    }

    /// <summary>Детерминированный ключ сравнения файлов: имя + размер + дата (UTC).</summary>
    public static string Fingerprint(SyncFileState state) =>
        $"{state.RelativePath}|{state.Size ?? 0}|{state.LastWriteUtc?.Ticks ?? 0}";
}

/// <summary>
/// Разрешение локальных файлов по шаблонам IncludePatterns (п.9, 1.0.0.91):
/// точные имена файлов в каталоге данных профиля и шаблоны «каталог/*.ext».
/// Файл ibases.v8i резолвится стандартным путём реестра 1С (или настроенным каталогом).
/// </summary>
public static class SettingsSyncFileResolver
{
    /// <summary>Разрешённый файл: относительное имя в хранилище + локальный путь.</summary>
    public sealed record ResolvedFile(string RelativePath, string LocalPath);

    /// <summary>Разрешает шаблоны в конкретные файлы (пути не проверяются на существование).</summary>
    public static List<ResolvedFile> Resolve(SettingsSyncSettings settings, string dataDirectory, string? ibasesPath)
    {
        var result = new List<ResolvedFile>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var rawPattern in settings.EffectiveIncludePatterns)
        {
            var pattern = (rawPattern ?? string.Empty).Trim().Replace('\\', '/');
            if (pattern.Length == 0)
                continue;

            // ibases.v8i — специальный случай: файл лежит в реестре 1С, не в каталоге данных.
            if (string.Equals(pattern, "ibases.v8i", StringComparison.OrdinalIgnoreCase))
            {
                var ibases = string.IsNullOrWhiteSpace(ibasesPath)
                    ? IbasesV8iImporter.FindDefaultPath()
                    : ibasesPath;
                if (!string.IsNullOrWhiteSpace(ibases))
                    Add(result, seen, "ibases.v8i", ibases);
                continue;
            }

            if (!SettingsSyncPackage.IsSafeRelativePath(pattern))
                continue;

            var star = pattern.IndexOf('*');
            if (star < 0)
            {
                var full = Path.Combine(dataDirectory, pattern.Replace('/', Path.DirectorySeparatorChar));
                Add(result, seen, pattern, full);
                continue;
            }

            // Шаблон каталога: «scripts/scenarios/*.script.json»
            var slash = pattern.LastIndexOf('/');
            var dirRelative = slash > 0 ? pattern.Substring(0, slash) : string.Empty;
            var filePattern = pattern.Substring(slash + 1);
            var dir = string.IsNullOrEmpty(dirRelative)
                ? dataDirectory
                : Path.Combine(dataDirectory, dirRelative.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(dir))
                continue;

            foreach (var file in Directory.EnumerateFiles(dir, filePattern))
            {
                var rel = (string.IsNullOrEmpty(dirRelative) ? string.Empty : dirRelative + "/") +
                          Path.GetFileName(file);
                Add(result, seen, rel, file);
            }
        }

        return result;
    }

    private static void Add(List<ResolvedFile> result, HashSet<string> seen, string rel, string fullPath)
    {
        if (!seen.Add(rel))
            return;
        result.Add(new ResolvedFile(rel, fullPath));
    }
}