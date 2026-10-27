#if WINDOWS
using System;
using System.Windows;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C;

/// <summary>
/// Окно «Уведомления в Telegram и по email» (функция №5, этап 2 — выпуск 0.3.12.69):
/// настройки бота Telegram, SMTP и фильтра событий; кнопки «Проверить» отправляют
/// тестовое сообщение с текущими (ещё не сохранёнными) значениями полей.
/// Сохраняет файл <c>notifications.json</c> через <see cref="INotificationSettingsStore"/>.
/// </summary>
public partial class NotificationChannelsWindow : Window
{
    private readonly INotificationSettingsStore _store = AppServices.GetRequiredService<INotificationSettingsStore>();
    private readonly TelegramNotificationChannel _telegram = AppServices.GetRequiredService<TelegramNotificationChannel>();
    private readonly EmailNotificationChannel _email = AppServices.GetRequiredService<EmailNotificationChannel>();
    private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();

    public NotificationChannelsWindow()
    {
        InitializeComponent();
        Title = LocalizationManager.T("Notifications.Title");
        Fill(_store.Load());
    }

    private void Fill(NotificationChannelSettings s)
    {
        TelegramEnabledCheck.IsChecked = s.TelegramEnabled;
        TelegramTokenBox.Password = s.TelegramBotToken;
        TelegramChatBox.Text = s.TelegramChatId;

        EmailEnabledCheck.IsChecked = s.EmailEnabled;
        SmtpHostBox.Text = s.SmtpHost;
        SmtpPortBox.Text = s.SmtpPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
        SmtpStartTlsCheck.IsChecked = s.SmtpUseStartTls;
        SmtpUserBox.Text = s.SmtpUser;
        SmtpPasswordBox.Password = s.SmtpPassword;
        EmailFromBox.Text = s.EmailFrom;
        EmailToBox.Text = s.EmailTo;

        OnBackupCheck.IsChecked = s.NotifyOnBackup;
        OnScheduledCheck.IsChecked = s.NotifyOnScheduledTasks;
        OnUpdatesCheck.IsChecked = s.NotifyOnUpdates;
        OnlyProblemsCheck.IsChecked = s.OnlyProblems;

        // Порог «свободно меньше N» (п.5 «Мониторинг лицензий», 1.0.0.87).
        LicenseThresholdBox.Text = s.LicenseFreeThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture);
        LicenseRepeatBox.Text = s.LicenseAlertRepeatHours.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private NotificationChannelSettings Collect()
    {
        var port = int.TryParse(SmtpPortBox.Text?.Trim(), out var p) ? p : 587;
        return NotificationSettingsStore.Normalize(new NotificationChannelSettings
        {
            TelegramEnabled = TelegramEnabledCheck.IsChecked == true,
            TelegramBotToken = TelegramTokenBox.Password ?? string.Empty,
            TelegramChatId = TelegramChatBox.Text ?? string.Empty,
            EmailEnabled = EmailEnabledCheck.IsChecked == true,
            SmtpHost = SmtpHostBox.Text ?? string.Empty,
            SmtpPort = port,
            SmtpUseStartTls = SmtpStartTlsCheck.IsChecked == true,
            SmtpUser = SmtpUserBox.Text ?? string.Empty,
            SmtpPassword = SmtpPasswordBox.Password ?? string.Empty,
            EmailFrom = EmailFromBox.Text ?? string.Empty,
            EmailTo = EmailToBox.Text ?? string.Empty,
            NotifyOnBackup = OnBackupCheck.IsChecked == true,
            NotifyOnScheduledTasks = OnScheduledCheck.IsChecked == true,
            NotifyOnUpdates = OnUpdatesCheck.IsChecked == true,
            OnlyProblems = OnlyProblemsCheck.IsChecked == true,

            // Порог «свободно меньше N» (п.5, 1.0.0.87): неотрицательное число, 0 — отключено.
            LicenseFreeThreshold = ParseNonNegative(LicenseThresholdBox.Text, 0),
            LicenseAlertRepeatHours = int.TryParse(LicenseRepeatBox.Text?.Trim(), out var repeat) && repeat > 0
                ? repeat
                : 24,
        });
    }

    private static int ParseNonNegative(string? text, int fallback) =>
        int.TryParse(text, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var v) && v >= 0 ? v : fallback;

    private static NotificationMessage TestMessage() => new()
    {
        Title = LocalizationManager.T("App.Title"),
        Message = LocalizationManager.T("Notifications.TestMessage"),
        Kind = NotificationKind.Info,
        Event = NotificationEvent.ManualTest
    };

    private async void OnTelegramTestClick(object sender, RoutedEventArgs e)
    {
        TelegramTestButton.IsEnabled = false;
        TelegramStatusText.Text = LocalizationManager.T("Notifications.Testing");
        try
        {
            var (ok, error) = await _telegram.TestAsync(Collect(), TestMessage());
            TelegramStatusText.Text = ok
                ? string.Format(LocalizationManager.T("Notifications.TestOk"), "Telegram")
                : string.Format(LocalizationManager.T("Notifications.TestFailed"), "Telegram", error);
        }
        catch (Exception ex)
        {
            TelegramStatusText.Text = string.Format(LocalizationManager.T("Notifications.TestFailed"), "Telegram", ex.Message);
        }
        finally
        {
            TelegramTestButton.IsEnabled = true;
        }
    }

    private async void OnEmailTestClick(object sender, RoutedEventArgs e)
    {
        EmailTestButton.IsEnabled = false;
        EmailStatusText.Text = LocalizationManager.T("Notifications.Testing");
        try
        {
            var (ok, error) = await _email.TestAsync(Collect(), TestMessage());
            EmailStatusText.Text = ok
                ? string.Format(LocalizationManager.T("Notifications.TestOk"), "email")
                : string.Format(LocalizationManager.T("Notifications.TestFailed"), "email", error);
        }
        catch (Exception ex)
        {
            EmailStatusText.Text = string.Format(LocalizationManager.T("Notifications.TestFailed"), "email", ex.Message);
        }
        finally
        {
            EmailTestButton.IsEnabled = true;
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            _store.Save(Collect());
            DialogResult = true;
        }
        catch (Exception ex)
        {
            _dialogs.ShowWarning(string.Format(LocalizationManager.T("Notifications.SaveFailed"), ex.Message),
                LocalizationManager.T("Notifications.Title"));
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
#endif
