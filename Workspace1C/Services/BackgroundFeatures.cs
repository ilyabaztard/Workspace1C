namespace Workspace1C.Services;

/// <summary>
/// Запуск и остановка фоновых функций выпуска 1.0.0.69 (мониторинг серверов
/// и журналов регистрации, команды Telegram-бота, синхронизация общего списка баз).
/// Вызывается приложением сразу после запуска планировщика и при выходе; сбой
/// любой функции не мешает остальным и запуску приложения.
/// </summary>
public static class BackgroundFeatures
{
    private static readonly List<Action> StopActions = new();

    public static void Start()
    {
        TryStart(() =>
        {
            var alerts = AppServices.GetRequiredService<ServerAlertsService>();
            alerts.Start();
            return alerts.Stop;
        });
        TryStart(() =>
        {
            var bot = AppServices.GetRequiredService<TelegramBotCommandService>();
            bot.Start();
            return bot.Stop;
        });
        // Синхронизация своих настроек (п.9, 1.0.0.91): фоновый таймер по интервалу
        // из настроек окна «Синхронизация настроек» (0 — только вручную).
        TryStart(() =>
        {
            var sync = AppServices.GetRequiredService<Sync.SettingsSyncRunner>();
            sync.Start();
            return sync.Stop;
        });
    }

    /// <summary>Регистрирует фоновую функцию: запуск возвращает действие остановки.</summary>
    public static void TryStart(Func<Action> start)
    {
        try
        {
            var stop = start();
            lock (StopActions)
                StopActions.Add(stop);
        }
        catch (Exception ex)
        {
            try { AppServices.TryGetService<IAppLogger>()?.Error("Ошибка запуска фоновой функции", ex); }
            catch { /* журнал недоступен */ }
        }
    }

    public static void Stop()
    {
        Action[] actions;
        lock (StopActions)
        {
            actions = StopActions.ToArray();
            StopActions.Clear();
        }
        foreach (var stop in actions)
        {
            try { stop(); }
            catch { /* остановка при выходе — без ошибок */ }
        }
    }
}
