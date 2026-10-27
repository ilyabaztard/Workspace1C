using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Реализация <see cref="INotificationSettingsStore"/> по образцу <see cref="ItsAccountsStore"/>:
/// читаемый JSON с отступами и кириллицей, атомарная запись, каталог — явный (тесты) →
/// каталог активного профиля → общий каталог данных приложения.
/// </summary>
public sealed class NotificationSettingsStore : INotificationSettingsStore
{
    /// <summary>Имя файла настроек каналов уведомлений.</summary>
    public const string FileName = "notifications.json";

    private readonly IProfileService? _profileService;
    private readonly string? _directoryOverride;
    private readonly IAppLogger? _logger;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <param name="profileService">Профиль для определения каталога данных (может быть null).</param>
    /// <param name="directoryOverride">Явный каталог хранения (юнит-тесты).</param>
    /// <param name="logger">Журнал (может быть null).</param>
    public NotificationSettingsStore(
        IProfileService? profileService = null,
        string? directoryOverride = null,
        IAppLogger? logger = null)
    {
        _profileService = profileService;
        _directoryOverride = directoryOverride;
        _logger = logger;
    }

    private string DataDirectory =>
        !string.IsNullOrWhiteSpace(_directoryOverride)
            ? _directoryOverride!
            : (!string.IsNullOrWhiteSpace(_profileService?.CurrentProfileDataDirectory)
                ? _profileService!.CurrentProfileDataDirectory
                : PlatformPaths.AppDataDirectory);

    /// <inheritdoc />
    public string FilePath => Path.Combine(DataDirectory, FileName);

    /// <inheritdoc />
    public NotificationChannelSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new NotificationChannelSettings();
            var json = File.ReadAllText(FilePath);
            var settings = JsonSerializer.Deserialize<NotificationChannelSettings>(json, _jsonOptions);
            return Normalize(settings ?? new NotificationChannelSettings());
        }
        catch (Exception ex)
        {
            _logger?.Warn($"Не удалось прочитать {FileName}: {ex.Message}");
            return new NotificationChannelSettings();
        }
    }

    /// <inheritdoc />
    public void Save(NotificationChannelSettings settings)
    {
        var normalized = Normalize(settings?.Clone() ?? new NotificationChannelSettings());
        Directory.CreateDirectory(DataDirectory);
        var json = JsonSerializer.Serialize(normalized, _jsonOptions);
        var tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, FilePath, overwrite: true);
    }

    /// <summary>Нормализует строки (null → пусто, обрезка пробелов), порт и порог лицензий.</summary>
    internal static NotificationChannelSettings Normalize(NotificationChannelSettings s)
    {
        s.TelegramBotToken = (s.TelegramBotToken ?? "").Trim();
        s.TelegramChatId = (s.TelegramChatId ?? "").Trim();
        s.SmtpHost = (s.SmtpHost ?? "").Trim();
        s.SmtpUser = (s.SmtpUser ?? "").Trim();
        s.SmtpPassword ??= "";
        s.EmailFrom = (s.EmailFrom ?? "").Trim();
        s.EmailTo = (s.EmailTo ?? "").Trim();
        if (s.SmtpPort <= 0 || s.SmtpPort > 65535)
            s.SmtpPort = 587;
        // Порог «свободно меньше N» (п.5, выпуск 1.0.0.87): отрицательные значения —
        // «отключено» (0). Период повторных оповещений — не меньше 1 часа.
        if (s.LicenseFreeThreshold < 0)
            s.LicenseFreeThreshold = 0;
        if (s.LicenseAlertRepeatHours < 1)
            s.LicenseAlertRepeatHours = 24;
        // Порог прогноза диска под копиями (п.7, выпуск 1.0.0.89): отрицательные — 0
        // (отключено); период повтора — не меньше 1 дня.
        if (s.DiskForecastThresholdDays < 0)
            s.DiskForecastThresholdDays = 0;
        if (s.DiskForecastAlertRepeatDays < 1)
            s.DiskForecastAlertRepeatDays = 1;
        return s;
    }
}
