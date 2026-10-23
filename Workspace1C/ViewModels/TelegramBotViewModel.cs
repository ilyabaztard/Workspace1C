using Workspace1C.Localization;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// ViewModel окна «Команды Telegram-бота» (выпуск 1.0.0.69): включение приёма команд,
/// разрешённые чаты и разрешение запуска заданий. Токен и чат берутся из настроек
/// уведомлений.
/// </summary>
public sealed class TelegramBotViewModel : ViewModelBase
{
    private bool _enabled;
    private string _allowedChatIds;
    private bool _allowRunTasks;
    private string _statusText = string.Empty;

    public TelegramBotViewModel(TelegramBotSettings settings, bool tokenConfigured, string notificationChatId)
    {
        _enabled = settings.Enabled;
        _allowedChatIds = settings.AllowedChatIds ?? string.Empty;
        _allowRunTasks = settings.AllowRunTasks;
        TokenConfigured = tokenConfigured;
        NotificationChatId = notificationChatId ?? string.Empty;
        HelpText = LocalizationManager.T("Bot.Help");
        TokenText = tokenConfigured
            ? string.Format(LocalizationManager.T("Bot.TokenOk"), string.IsNullOrWhiteSpace(NotificationChatId) ? "—" : NotificationChatId)
            : LocalizationManager.T("Bot.TokenMissing");
    }

    public bool TokenConfigured { get; }

    public string NotificationChatId { get; }

    public string HelpText { get; }

    public string TokenText { get; }

    public bool Enabled { get => _enabled; set => SetProperty(ref _enabled, value); }

    public string AllowedChatIds { get => _allowedChatIds; set => SetProperty(ref _allowedChatIds, value ?? string.Empty); }

    public bool AllowRunTasks { get => _allowRunTasks; set => SetProperty(ref _allowRunTasks, value); }

    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    public TelegramBotSettings BuildSettings() => new()
    {
        Enabled = Enabled,
        AllowedChatIds = AllowedChatIds.Trim(),
        AllowRunTasks = AllowRunTasks
    };

    public void Save()
    {
        TelegramBotCommandService.Save(BuildSettings());
        StatusText = LocalizationManager.T(Enabled && !TokenConfigured ? "Bot.SavedNoToken" : "Bot.Saved");
    }
}
