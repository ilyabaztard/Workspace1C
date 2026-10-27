using System.Collections.ObjectModel;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Services.Sync;

namespace Workspace1C.ViewModels;

/// <summary>
/// ViewModel окна «Синхронизация настроек» (п.9, 1.0.0.91): настройка хранилища
/// (WebDAV / сетевая папка), интервал автозапуска, набор файлов, кнопка
/// «Синхронизировать сейчас» и журнал операций. Чистый .NET без WPF/Avalonia
/// зависимостей (используется обеими платформами). Пароль WebDAV вводится в поле,
/// сохраняется ТОЛЬКО зашифрованным (<see cref="NoteCrypto"/>) и в журнал не попадает.
/// </summary>
public sealed class SettingsSyncViewModel : ViewModelBase
{
    private readonly SettingsSyncService _service;
    private SettingsSyncSettings _settings = new();

    public SettingsSyncViewModel(SettingsSyncService? service = null)
    {
        _service = service ?? new SettingsSyncService();
        Load();
    }

    /// <summary>Варианты хранилища для выпадающего списка (ключ enum → локализованное имя).</summary>
    public IReadOnlyList<KeyValuePair<string, string>> StorageKindOptions { get; } = new[]
    {
        new KeyValuePair<string, string>("WebDav", LocalizationManager.T("SettingsSync.Storage.WebDav")),
        new KeyValuePair<string, string>("NetworkFolder", LocalizationManager.T("SettingsSync.Storage.Folder"))
    };

    /// <summary>Журнал последней операции (без секретов).</summary>
    public ObservableCollection<string> LogEntries { get; } = new();

    private bool _enabled;
    public bool Enabled
    {
        get => _enabled;
        set => SetProperty(ref _enabled, value);
    }

    private int _storageKindIndex;
    public int StorageKindIndex
    {
        get => _storageKindIndex;
        set => SetProperty(ref _storageKindIndex, value);
    }

    private string _webDavUrl = string.Empty;
    public string WebDavUrl
    {
        get => _webDavUrl;
        set => SetProperty(ref _webDavUrl, value ?? string.Empty);
    }

    private string _folderPath = string.Empty;
    public string FolderPath
    {
        get => _folderPath;
        set => SetProperty(ref _folderPath, value ?? string.Empty);
    }

    private string _user = string.Empty;
    public string User
    {
        get => _user;
        set => SetProperty(ref _user, value ?? string.Empty);
    }

    /// <summary>Вводимый пароль WebDAV (не хранится открыто, после сохранения очищается).</summary>
    private string _password = string.Empty;
    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value ?? string.Empty);
    }

    private string _intervalMinutesText = "60";
    public string IntervalMinutesText
    {
        get => _intervalMinutesText;
        set => SetProperty(ref _intervalMinutesText, value ?? string.Empty);
    }

    private string _patternsText = string.Empty;
    public string PatternsText
    {
        get => _patternsText;
        set => SetProperty(ref _patternsText, value ?? string.Empty);
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetPropertyWithRelated(ref _isBusy, value, nameof(IsBusy), nameof(CanSyncNow));
    }

    /// <summary>Синхронизация не выполняется прямо сейчас (для кнопки).</summary>
    public bool CanSyncNow => !IsBusy;

    private string _lastSyncAtText = "—";
    public string LastSyncAtText
    {
        get => _lastSyncAtText;
        private set => SetProperty(ref _lastSyncAtText, value ?? "—");
    }

    private string _statusText = string.Empty;
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value ?? string.Empty);
    }

    /// <summary>Загружает настройки из хранилища в поля окна.</summary>
    public void Load()
    {
        _settings = SettingsSyncStore.Load();

        Enabled = _settings.Enabled;
        StorageKindIndex = _settings.StorageKind == SettingsSyncStorageKind.NetworkFolder ? 1 : 0;
        WebDavUrl = _settings.WebDavUrl;
        FolderPath = _settings.FolderPath;
        User = _settings.User;
        Password = string.Empty;
        IntervalMinutesText = _settings.IntervalMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture);
        PatternsText = string.Join(Environment.NewLine, _settings.EffectiveIncludePatterns);
        LastSyncAtText = _settings.LastSyncAt.HasValue
            ? _settings.LastSyncAt.Value.ToLocalTime().ToString("g")
            : LocalizationManager.T("SettingsSync.Never");
        StatusText = _settings.LastResult;
        LogEntries.Clear();
    }

    /// <summary>
    /// Сохраняет поля окна в хранилище. Новый пароль шифруется; пустое поле не
    /// трогает ранее сохранённый зашифрованный пароль.
    /// </summary>
    public void Save()
    {
        _settings.Enabled = Enabled;
        _settings.StorageKind = StorageKindIndex == 1
            ? SettingsSyncStorageKind.NetworkFolder
            : SettingsSyncStorageKind.WebDav;
        _settings.WebDavUrl = (WebDavUrl ?? string.Empty).Trim();
        _settings.FolderPath = (FolderPath ?? string.Empty).Trim();
        _settings.User = (User ?? string.Empty).Trim();

        if (!string.IsNullOrWhiteSpace(Password))
            _settings.PasswordEncrypted = _service.EncryptPassword(Password);

        _settings.IntervalMinutes = int.TryParse(IntervalMinutesText?.Trim(),
            System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var minutes)
            ? Math.Max(0, minutes)
            : 0;

        var patterns = (PatternsText ?? string.Empty)
            .Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0 && SettingsSyncPackage.IsSafeRelativePath(p))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        _settings.IncludePatterns = patterns;

        SettingsSyncStore.Save(_settings);
        Password = string.Empty;
    }

    /// <summary>Сохраняет настройки и выполняет синхронизацию. Журнал — без секретов.</summary>
    public async Task SyncNowAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        LogEntries.Clear();
        try
        {
            Save();
            var settings = SettingsSyncStore.Load();

            var validation = SettingsSyncService.Validate(settings);
            if (validation.Count > 0)
            {
                foreach (var key in validation)
                    LogEntries.Add(LocalizationManager.T(key));
                StatusText = LocalizationManager.T("SettingsSync.ValidationFailed");
                return;
            }

            LogEntries.Add(LocalizationManager.T("SettingsSync.Started"));
            var result = await _service.SyncAsync(settings, line => LogEntries.Add(line)).ConfigureAwait(false);

            // Обновляем LastSyncAt/LastResult из сохранённого после синка состояния.
            var updated = SettingsSyncStore.Load();
            _settings = updated;
            LastSyncAtText = updated.LastSyncAt.HasValue
                ? updated.LastSyncAt.Value.ToLocalTime().ToString("g")
                : LocalizationManager.T("SettingsSync.Never");
            StatusText = result.Message;

            if (result.Errors.Count > 0)
            {
                LogEntries.Add(string.Format(LocalizationManager.T("SettingsSync.ErrorsCount"), result.Errors.Count));
                foreach (var error in result.Errors.Take(20))
                    LogEntries.Add("• " + error);
            }
            if (result.Conflicts.Count > 0)
            {
                LogEntries.Add(string.Format(LocalizationManager.T("SettingsSync.ConflictCount"), result.Conflicts.Count));
                foreach (var conflict in result.Conflicts.Take(20))
                    LogEntries.Add("• " + conflict);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }
}