using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.Services.Sync;

/// <summary>
/// Ядро двусторонней синхронизации своих настроек между компьютерами
/// (п.9 «Синхронизация настроек», 1.0.0.91): разрешение локальных файлов, опрос
/// удалённого хранилища (WebDAV или сетевая папка), план last-write-wins
/// (<see cref="SettingsSyncPlan.Build"/>) и применение: выгрузка/скачивание с
/// сохранением проигравшей версии как <c>*.conflict-<дата></c> в каталоге
/// <c>settings_sync_conflicts</c> каталога данных профиля.
///
/// Выбран ПФАЙЛОВЫЙ транспорт (не один zip): это даёт last-write-wins на уровне
/// каждого файла, меньший сетевой трафик (передаётся только изменившийся файл) и
/// простой журнал операций. Zip-пакет (<see cref="SettingsSyncPackage"/>) существует
/// как вспомогательный инструмент снимка и покрыт тестами (в т.ч. защита zip-slip).
///
/// Секреты: пароль WebDAV хранится зашифрованным (<see cref="NoteCrypto"/>) и не
/// попадает в журнал/результат; вывод полностью маскирован.
/// </summary>
public class SettingsSyncService
{
    /// <summary>Имя служебного файла метаданных на удалённой стороне (без секретов).</summary>
    public const string RemoteMetaName = "_workspace1c_sync_meta.json";

    /// <summary>Подкаталог конфликтных резервных копий в каталоге данных профиля.</summary>
    public const string ConflictsSubDir = "settings_sync_conflicts";

    private readonly IAppLogger? _logger;
    private readonly string? _dataDirectoryOverride;
    private readonly NoteCrypto _crypto;
    private readonly Func<DateTime> _nowUtc;
    private readonly string? _configuredIbasesPath;

    /// <param name="logger">Журнал приложения (может отсутствовать — например, в тестах).</param>
    /// <param name="dataDirectoryOverride">Каталог данных профиля (тесты); null — реальный.</param>
    /// <param name="crypto">Шифровальщик пароля; null — платформенный по умолчанию.</param>
    /// <param name="nowUtc">Источник текущего UTC-времени (тесты); null — DateTime.UtcNow.</param>
    /// <param name="configuredIbasesPath">Пользовательский путь ibases.v8i (из настроек приложения).</param>
    public SettingsSyncService(IAppLogger? logger = null, string? dataDirectoryOverride = null,
        NoteCrypto? crypto = null, Func<DateTime>? nowUtc = null, string? configuredIbasesPath = null)
    {
        _logger = logger;
        _dataDirectoryOverride = dataDirectoryOverride;
        _crypto = crypto ?? NoteCrypto.CreateDefault();
        _nowUtc = nowUtc ?? (() => DateTime.UtcNow);
        _configuredIbasesPath = configuredIbasesPath;
    }

    private string DataDirectory =>
        string.IsNullOrWhiteSpace(_dataDirectoryOverride)
            ? FeatureDataStore.DataDirectory
            : _dataDirectoryOverride;

    private string ConflictsDirectory => Path.Combine(DataDirectory, ConflictsSubDir);

    /// <summary>Каталог данных: переопределение для тестов/отображения.</summary>
    public string SyncDataDirectory => DataDirectory;

    /// <summary>Есть ли настроенный ibases.v8i (стандартный или из настроек).</summary>
    public string? ResolveIbasesPath() => string.IsNullOrWhiteSpace(_configuredIbasesPath)
        ? IbasesV8iImporter.FindDefaultPath()
        : _configuredIbasesPath;

    /// <summary>
    /// Валидация настроек: возвращает список ключей локализации ошибок (пусто — всё корректно).
    /// Пароли не проверяются и не раскрываются.
    /// </summary>
    public static List<string> Validate(SettingsSyncSettings settings)
    {
        var errors = new List<string>();
        if (settings is null)
        {
            errors.Add("SettingsSync.StorageRequired");
            return errors;
        }

        if (settings.StorageKind == SettingsSyncStorageKind.NetworkFolder)
        {
            if (string.IsNullOrWhiteSpace(settings.FolderPath))
                errors.Add("SettingsSync.FolderRequired");
        }
        else
        {
            var url = settings.WebDavUrl?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(url))
                errors.Add("SettingsSync.WebDavUrlRequired");
            else if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                     (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                errors.Add("SettingsSync.WebDavUrlInvalid");
        }

        if (settings.IntervalMinutes < 0)
            errors.Add("SettingsSync.IntervalInvalid");
        return errors;
    }

    /// <summary>Расшифрованный пароль WebDAV (пусто — пароля нет). Ошибки ключа не роняют.</summary>
    public string DecryptPassword(SettingsSyncSettings settings)
    {
        if (string.IsNullOrEmpty(settings?.PasswordEncrypted))
            return string.Empty;
        var result = _crypto.Decrypt(settings.PasswordEncrypted);
        return result.Value ?? string.Empty;
    }

    /// <summary>Шифрует пароль для хранения (пусто — не шифровать).</summary>
    public string EncryptPassword(string? plainPassword)
    {
        if (string.IsNullOrEmpty(plainPassword))
            return string.Empty;
        var result = _crypto.Encrypt(plainPassword);
        return result.Value ?? string.Empty;
    }

    /// <summary>
    /// Выполняет синхронизацию по настройкам. Каждое событие передаётся в <paramref name="log"/>
    /// (без секретов). Возвращает результат с журналом операций.
    /// </summary>
    public async Task<SettingsSyncResult> SyncAsync(SettingsSyncSettings settings,
        Action<string>? log = null, CancellationToken ct = default)
    {
        var result = new SettingsSyncResult();
        var errors = Validate(settings);
        if (errors.Count > 0)
        {
            foreach (var key in errors)
                result.AddError(key);
            result.Success = false;
            result.Message = LocalizationManager.T("SettingsSync.ValidationFailed");
            log?.Invoke(result.Message);
            return result;
        }

        // --- 1. Локальные файлы ---
        var resolved = SettingsSyncFileResolver.Resolve(settings, DataDirectory, ResolveIbasesPath());
        var localStates = resolved.Select(r =>
        {
            DateTime? lastWrite = null;
            long? size = null;
            try
            {
                if (File.Exists(r.LocalPath))
                {
                    lastWrite = File.GetLastWriteTimeUtc(r.LocalPath);
                    size = new FileInfo(r.LocalPath).Length;
                }
            }
            catch
            {
                // Битый файл — пропускаем сторону, удалённая останется источником.
            }
            return new SyncFileState(r.RelativePath, lastWrite, size);
        }).ToList();

        // --- 2. Удалённые состояния (WebDAV: HEAD/Last-Modified; папка: FileInfo) ---
        var remoteStates = new List<SyncFileState>();
        var remoteMeta = await LoadRemoteMetaAsync(settings, log, ct).ConfigureAwait(false);
        foreach (var r in resolved)
        {
            var state = await ProbeRemoteAsync(settings, r.RelativePath, remoteMeta, ct).ConfigureAwait(false);
            if (state is not null)
                remoteStates.Add(state);
        }

        // --- 3. План послед-записавший-побеждает ---
        var plan = SettingsSyncPlan.Build(localStates, remoteStates);
        var changed = plan.Where(p => p.Operation != SyncOperation.Skip).ToList();

        log?.Invoke(string.Format(
            LocalizationManager.T("SettingsSync.PlanSummary"),
            resolved.Count, changed.Count));

        if (changed.Count == 0)
        {
            await WriteRemoteMetaAsync(settings, remoteMeta, ct).ConfigureAwait(false);
            result.Success = true;
            result.Message = LocalizationManager.T("SettingsSync.NothingToSync");
            result.LastSyncUtc = _nowUtc();
            log?.Invoke(result.Message);
            return result;
        }

        // --- 4. Применение ---
        var now = _nowUtc();
        var updatedMeta = remoteMeta with { Files = new Dictionary<string, RemoteFileMeta>(remoteMeta.Files) };

        foreach (var entry in changed)
        {
            if (ct.IsCancellationRequested)
                break;

            var resolvedFile = resolved.FirstOrDefault(f =>
                string.Equals(f.RelativePath, entry.RelativePath, StringComparison.Ordinal));

            try
            {
                if (entry.Operation == SyncOperation.Upload && resolvedFile is not null && File.Exists(resolvedFile.LocalPath))
                {
                    // Проигравшая удалённая версия — в конфликтный резерв перед перезаписью.
                    if (entry.NeedsConflictBackup)
                    {
                        var backup = await BackupRemoteAsync(settings, entry, now, ct).ConfigureAwait(false);
                        if (backup is not null)
                            result.Conflicts.Add(backup);
                    }
                    var error = await UploadAsync(settings, resolvedFile.LocalPath, entry.RelativePath, ct).ConfigureAwait(false);
                    if (error is not null)
                    {
                        result.AddError("SettingsSync.FileError", entry.RelativePath, error);
                        continue;
                    }
                    var fi = new FileInfo(resolvedFile.LocalPath);
                    updatedMeta.Files[entry.RelativePath] = new RemoteFileMeta(fi.Length, File.GetLastWriteTimeUtc(resolvedFile.LocalPath));
                    result.ChangedFiles.Add(entry.RelativePath);
                    log?.Invoke(string.Format(LocalizationManager.T("SettingsSync.Uploaded"), entry.RelativePath));
                }
                else if (entry.Operation == SyncOperation.Download && resolvedFile is not null)
                {
                    // Проигравшая локальная версия — в конфликтный резерв перед перезаписью.
                    if (entry.NeedsConflictBackup && File.Exists(resolvedFile.LocalPath))
                    {
                        var backupName = SettingsSyncPlan.ConflictBackupName(entry.RelativePath, "local", now);
                        var backupPath = Path.Combine(ConflictsDirectory, backupName);
                        Directory.CreateDirectory(ConflictsDirectory);
                        File.Copy(resolvedFile.LocalPath, backupPath, overwrite: true);
                        result.Conflicts.Add(backupName);
                        log?.Invoke(string.Format(LocalizationManager.T("SettingsSync.ConflictBackup"), backupName));
                    }

                    var error = await DownloadAsync(settings, entry.RelativePath, resolvedFile.LocalPath, ct).ConfigureAwait(false);
                    if (error is not null)
                    {
                        result.AddError("SettingsSync.FileError", entry.RelativePath, error);
                        continue;
                    }
                    long downloadedSize;
                    try { downloadedSize = new FileInfo(resolvedFile.LocalPath).Length; }
                    catch { downloadedSize = 0; }
                    updatedMeta.Files[entry.RelativePath] = new RemoteFileMeta(
                        downloadedSize, entry.RemoteLastWriteUtc ?? now);
                    result.ChangedFiles.Add(entry.RelativePath);
                    log?.Invoke(string.Format(LocalizationManager.T("SettingsSync.Downloaded"), entry.RelativePath));
                }
            }
            catch (Exception ex)
            {
                _logger?.Error($"Синхронизация настроек: файл «{entry.RelativePath}»", ex);
                result.AddError("SettingsSync.FileError", entry.RelativePath, ex.Message);
            }
        }

        // --- 5. Метаданные на удалённой стороне + итог ---
        if (result.Errors.Count == 0)
        {
            await WriteRemoteMetaAsync(settings, updatedMeta, ct).ConfigureAwait(false);
        }

        result.LastSyncUtc = _nowUtc();
        result.Success = result.Errors.Count == 0;
        result.Message = result.Success
            ? string.Format(LocalizationManager.T("SettingsSync.Done"),
                result.ChangedFiles.Count, result.Conflicts.Count)
            : string.Format(LocalizationManager.T("SettingsSync.DoneWithErrors"),
                result.ChangedFiles.Count, result.Errors.Count);
        log?.Invoke(result.Message);
        return result;
    }

    // ===================== Storage primitives =====================

    private string RemoteRoot(SettingsSyncSettings settings) =>
        settings.StorageKind == SettingsSyncStorageKind.WebDav
            ? (settings.WebDavUrl ?? string.Empty).TrimEnd('/')
            : (settings.FolderPath ?? string.Empty).TrimEnd('/', '\\');

    private string RemoteFilePath(SettingsSyncSettings settings, string relativePath) =>
        settings.StorageKind == SettingsSyncStorageKind.WebDav
            ? WebDavClient.CombineUrl(RemoteRoot(settings), relativePath)
            : Path.Combine(RemoteRoot(settings), relativePath.Replace('/', Path.DirectorySeparatorChar));

    private async Task<SyncFileState?> ProbeRemoteAsync(SettingsSyncSettings settings, string relativePath,
        RemoteMeta meta, CancellationToken ct)
    {
        try
        {
            if (settings.StorageKind == SettingsSyncStorageKind.WebDav)
            {
                var user = settings.User;
                var pass = DecryptPassword(settings);
                var url = RemoteFilePath(settings, relativePath);
                var probe = await WebDavClient.ProbeAsync(url, user, pass, ct).ConfigureAwait(false);
                if (!probe.Exists)
                    return null;

                // Логическое время версии: приоритет — дата последнего изменения с машины-источника
                // (метаданные), иначе — Last-Modified сервера WebDAV (только транспортное время).
                var logical = meta.Files.TryGetValue(relativePath, out var m)
                    ? m.LastWriteUtc
                    : probe.LastModifiedUtc;
                return new SyncFileState(relativePath, logical, probe.Length ?? m?.Size);
            }
            else
            {
                var path = RemoteFilePath(settings, relativePath);
                if (!File.Exists(path))
                    return null;
                var info = new FileInfo(path);
                return new SyncFileState(relativePath, info.LastWriteTimeUtc, info.Length);
            }
        }
        catch (Exception ex)
        {
            _logger?.Warn($"Синхронизация настроек: не удалось опросить «{relativePath}»: {ex.Message}");
            return null;
        }
    }

    private async Task<string?> UploadAsync(SettingsSyncSettings settings, string localPath, string relativePath, CancellationToken ct)
    {
        if (settings.StorageKind == SettingsSyncStorageKind.WebDav)
        {
            var user = settings.User;
            var pass = DecryptPassword(settings);
            var url = RemoteFilePath(settings, relativePath);
            return await WebDavClient.UploadAsync(localPath, url, user, pass, ct).ConfigureAwait(false);
        }

        var destination = RemoteFilePath(settings, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? string.Empty);
        File.Copy(localPath, destination, overwrite: true);
        // Сохраняем время последнего изменения файла-источника: last-write-wins работает
        // между машинами и без метаданных (папка общего доступа).
        try { File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(localPath)); }
        catch { /* права на запись времени — опционально */ }
        return null;
    }

    private async Task<string?> DownloadAsync(SettingsSyncSettings settings, string relativePath, string localPath, CancellationToken ct)
    {
        if (settings.StorageKind == SettingsSyncStorageKind.WebDav)
        {
            var user = settings.User;
            var pass = DecryptPassword(settings);
            var url = RemoteFilePath(settings, relativePath);
            var error = await WebDavClient.DownloadAsync(url, user, pass, localPath, ct).ConfigureAwait(false);
            return error;
        }

        var source = RemoteFilePath(settings, relativePath);
        var dir = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.Copy(source, localPath, overwrite: true);
        // Аналогично выгрузке: переносим дату логической версии, чтобы не было «пинг-понга».
        try { File.SetLastWriteTimeUtc(localPath, File.GetLastWriteTimeUtc(source)); }
        catch { /* опционально */ }
        return null;
    }

    private async Task<string?> BackupRemoteAsync(SettingsSyncSettings settings, SyncPlanEntry entry,
        DateTime now, CancellationToken ct)
    {
        var backupName = SettingsSyncPlan.ConflictBackupName(entry.RelativePath, "remote", now);
        var backupPath = Path.Combine(ConflictsDirectory, backupName);
        Directory.CreateDirectory(ConflictsDirectory);

        if (settings.StorageKind == SettingsSyncStorageKind.WebDav)
        {
            var error = await DownloadAsync(settings, entry.RelativePath, backupPath, ct).ConfigureAwait(false);
            if (error is not null)
                return null;
        }
        else
        {
            var source = RemoteFilePath(settings, entry.RelativePath);
            if (File.Exists(source))
                File.Copy(source, backupPath, overwrite: true);
            else
                return null;
        }

        _logger?.Info($"Синхронизация настроек: конфликтный резерв удалённой версии «{backupName}»");
        return backupName;
    }

    // ===================== Remote metadata =====================

    /// <summary>Метаданные удалённой стороны: машина, версия, даты последних версий файлов.</summary>
    public sealed record RemoteMeta(
        string Machine,
        string AppVersion,
        DateTime? LastSyncUtc,
        Dictionary<string, RemoteFileMeta> Files);

    /// <summary>Запись метаданных файла: размер и логическое время версии (UTC, источник).</summary>
    public sealed record RemoteFileMeta(long Size, DateTime LastWriteUtc);

    private sealed class MetaDto
    {
        public string Machine { get; set; } = string.Empty;
        public string AppVersion { get; set; } = string.Empty;
        public DateTime? LastSyncUtc { get; set; }
        public List<MetaFileDto> Files { get; set; } = new();
    }

    private sealed class MetaFileDto
    {
        public string Name { get; set; } = string.Empty;
        public long Size { get; set; }
        public DateTime LastWriteUtc { get; set; }
    }

    private static readonly JsonSerializerOptions MetaDtoOptions = new() { WriteIndented = true };

    private async Task<RemoteMeta> LoadRemoteMetaAsync(SettingsSyncSettings settings, Action<string>? log, CancellationToken ct)
    {
        try
        {
            if (settings.StorageKind == SettingsSyncStorageKind.WebDav)
            {
                var temp = Path.Combine(Path.GetTempPath(), "workspace1c_sync_meta_" + Guid.NewGuid().ToString("N") + ".json");
                var error = await WebDavClient.DownloadAsync(
                    RemoteFilePath(settings, RemoteMetaName), settings.User, DecryptPassword(settings), temp, ct).ConfigureAwait(false);
                if (error is not null)
                    return new RemoteMeta(string.Empty, string.Empty, null, new Dictionary<string, RemoteFileMeta>());
                try
                {
                    var dto = JsonSerializer.Deserialize<MetaDto>(await File.ReadAllTextAsync(temp, ct).ConfigureAwait(false), MetaDtoOptions);
                    if (dto is null)
                        return new RemoteMeta(string.Empty, string.Empty, null, new Dictionary<string, RemoteFileMeta>());
                    var files = dto.Files
                        .Where(f => SettingsSyncPackage.IsSafeRelativePath(f.Name))
                        .ToDictionary(f => f.Name, f => new RemoteFileMeta(f.Size, f.LastWriteUtc), StringComparer.Ordinal);
                    return new RemoteMeta(dto.Machine, dto.AppVersion, dto.LastSyncUtc, files);
                }
                finally
                {
                    try { File.Delete(temp); } catch { /* без удаления — тоже ок */ }
                }
            }
            else
            {
                var path = RemoteFilePath(settings, RemoteMetaName);
                if (!File.Exists(path))
                    return new RemoteMeta(string.Empty, string.Empty, null, new Dictionary<string, RemoteFileMeta>());
                var dto = JsonSerializer.Deserialize<MetaDto>(await File.ReadAllTextAsync(path, ct).ConfigureAwait(false), MetaDtoOptions);
                if (dto is null)
                    return new RemoteMeta(string.Empty, string.Empty, null, new Dictionary<string, RemoteFileMeta>());
                var files = dto.Files
                    .Where(f => SettingsSyncPackage.IsSafeRelativePath(f.Name))
                    .ToDictionary(f => f.Name, f => new RemoteFileMeta(f.Size, f.LastWriteUtc), StringComparer.Ordinal);
                return new RemoteMeta(dto.Machine, dto.AppVersion, dto.LastSyncUtc, files);
            }
        }
        catch (Exception ex)
        {
            _logger?.Warn($"Синхронизация настроек: не удалось прочитать метаданные: {ex.Message}");
            return new RemoteMeta(string.Empty, string.Empty, null, new Dictionary<string, RemoteFileMeta>());
        }
    }

    private async Task WriteRemoteMetaAsync(SettingsSyncSettings settings, RemoteMeta meta, CancellationToken ct)
    {
        try
        {
            var dto = new MetaDto
            {
                Machine = Environment.MachineName,
                AppVersion = VersionInfo.Display(),
                LastSyncUtc = _nowUtc(),
                Files = meta.Files
                    .Select(f => new MetaFileDto { Name = f.Key, Size = f.Value.Size, LastWriteUtc = f.Value.LastWriteUtc })
                    .OrderBy(f => f.Name, StringComparer.Ordinal)
                    .ToList()
            };
            var json = JsonSerializer.Serialize(dto, MetaDtoOptions);

            if (settings.StorageKind == SettingsSyncStorageKind.WebDav)
            {
                var temp = Path.Combine(Path.GetTempPath(), "workspace1c_sync_meta_" + Guid.NewGuid().ToString("N") + ".json");
                await File.WriteAllTextAsync(temp, json, ct).ConfigureAwait(false);
                try
                {
                    await WebDavClient.UploadAsync(temp, RemoteFilePath(settings, RemoteMetaName),
                        settings.User, DecryptPassword(settings), ct).ConfigureAwait(false);
                }
                finally
                {
                    try { File.Delete(temp); } catch { /* временный файл */ }
                }
            }
            else
            {
                var path = RemoteFilePath(settings, RemoteMetaName);
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
                await File.WriteAllTextAsync(path, json, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger?.Warn($"Синхронизация настроек: не удалось записать метаданные: {ex.Message}");
        }
    }
}