using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// ViewModel окна «Мониторинг и оповещения» (выпуск 1.0.0.69): пороги для серверов
/// из справочника «Серверы 1С» и наблюдение за ошибками журналов регистрации файловых
/// баз; сохранение настроек и проверка «сейчас».
/// </summary>
public sealed class ServerAlertsViewModel : ViewModelBase
{
    private readonly ServerAlertsService? _service;
    private readonly StringBuilder _log = new();
    private bool _enabled;
    private string _intervalText;
    private string _repeatText;
    private bool _isBusy;

    public ServerAlertsViewModel(IEnumerable<ManagementServer> servers, IEnumerable<Infobase> infobases,
        ServerAlertSettings settings, ServerAlertsService? service)
    {
        _service = service;
        _enabled = settings.Enabled;
        _intervalText = settings.IntervalMinutes.ToString(CultureInfo.InvariantCulture);
        _repeatText = settings.RepeatMinutes.ToString(CultureInfo.InvariantCulture);

        foreach (var server in servers ?? Array.Empty<ManagementServer>())
        {
            var rule = settings.Rules.FirstOrDefault(r => r.ServerId == server.Id);
            Servers.Add(new ServerAlertRow(server, rule));
        }
        foreach (var ib in (infobases ?? Array.Empty<Infobase>())
                     .Where(b => b is not null && b.Connection.Type == ConnectionType.File)
                     .OrderBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var watch = settings.EventLogWatches.FirstOrDefault(w => w.InfobaseId == ib.Id);
            EventLogs.Add(new EventLogWatchRow(ib, watch));
        }
    }

    public ObservableCollection<ServerAlertRow> Servers { get; } = new();

    public ObservableCollection<EventLogWatchRow> EventLogs { get; } = new();

    public bool Enabled { get => _enabled; set => SetProperty(ref _enabled, value); }

    public string IntervalText { get => _intervalText; set => SetProperty(ref _intervalText, value ?? string.Empty); }

    public string RepeatText { get => _repeatText; set => SetProperty(ref _repeatText, value ?? string.Empty); }

    public bool HasServers => Servers.Count > 0;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
                OnPropertyChanged(nameof(IsIdle));
        }
    }

    public bool IsIdle => !_isBusy;

    public string LogText => _log.ToString();

    public void AppendLog(string message)
    {
        _log.Append(DateTime.Now.ToString("HH:mm:ss")).Append("  ").AppendLine(message);
        OnPropertyChanged(nameof(LogText));
    }

    private static int ParseNonNegative(string? text, int fallback) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v >= 0 ? v : fallback;

    /// <summary>Настройки по текущему состоянию формы.</summary>
    public ServerAlertSettings BuildSettings() => new()
    {
        Enabled = Enabled,
        IntervalMinutes = Math.Max(1, ParseNonNegative(IntervalText, 5)),
        RepeatMinutes = Math.Max(1, ParseNonNegative(RepeatText, 60)),
        Rules = Servers.Where(s => s.Enabled || s.HasThresholds).Select(s => s.ToRule()).ToList(),
        EventLogWatches = EventLogs.Where(e => e.Enabled).Select(e => e.ToWatch()).ToList()
    };

    public void Save()
    {
        ServerAlertsService.Save(BuildSettings());
        AppendLog(LocalizationManager.T("Alerts.Saved"));
    }

    /// <summary>Проверяет пороги сейчас (без отправки оповещений) и выводит результат в журнал.</summary>
    public async Task CheckNowAsync()
    {
        if (IsBusy || _service is null)
            return;
        IsBusy = true;
        try
        {
            var settings = BuildSettings();
            settings.Enabled = true;
            AppendLog(LocalizationManager.T("Alerts.Checking"));
            var alerts = await _service.CheckNowAsync(settings, notify: false);
            if (alerts.Count == 0)
                AppendLog(LocalizationManager.T("Alerts.AllGood"));
            foreach (var text in alerts)
                AppendLog(text);
        }
        catch (Exception ex)
        {
            AppendLog(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>Строка таблицы серверов окна мониторинга.</summary>
public sealed class ServerAlertRow : ViewModelBase
{
    private bool _enabled;
    private string _maxSessions;
    private string _maxMemory;
    private string _maxLocks;

    public ServerAlertRow(ManagementServer server, ServerAlertRule? rule)
    {
        Server = server;
        _enabled = rule?.Enabled ?? false;
        _maxSessions = (rule?.MaxSessions ?? 0).ToString(CultureInfo.InvariantCulture);
        _maxMemory = (rule?.MaxProcessMemoryMb ?? 0).ToString(CultureInfo.InvariantCulture);
        _maxLocks = (rule?.MaxWaitingLocks ?? 0).ToString(CultureInfo.InvariantCulture);
    }

    public ManagementServer Server { get; }

    public string Name => Server.ToString();

    public bool Enabled { get => _enabled; set => SetProperty(ref _enabled, value); }

    public string MaxSessions { get => _maxSessions; set => SetProperty(ref _maxSessions, value ?? string.Empty); }

    public string MaxMemoryMb { get => _maxMemory; set => SetProperty(ref _maxMemory, value ?? string.Empty); }

    public string MaxWaitingLocks { get => _maxLocks; set => SetProperty(ref _maxLocks, value ?? string.Empty); }

    private static int Parse(string text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : 0;

    public bool HasThresholds => Parse(_maxSessions) > 0 || Parse(_maxMemory) > 0 || Parse(_maxLocks) > 0;

    public ServerAlertRule ToRule() => new()
    {
        ServerId = Server.Id,
        Enabled = Enabled,
        MaxSessions = Parse(_maxSessions),
        MaxProcessMemoryMb = Parse(_maxMemory),
        MaxWaitingLocks = Parse(_maxLocks)
    };
}

/// <summary>Строка таблицы наблюдения за журналами регистрации.</summary>
public sealed class EventLogWatchRow : ViewModelBase
{
    private bool _enabled;
    private string _minErrors;

    public EventLogWatchRow(Infobase infobase, EventLogWatch? watch)
    {
        Infobase = infobase;
        _enabled = watch?.Enabled ?? false;
        _minErrors = (watch?.MinErrors ?? 1).ToString(CultureInfo.InvariantCulture);
    }

    public Infobase Infobase { get; }

    public string Name => Infobase.Name;

    public bool Enabled { get => _enabled; set => SetProperty(ref _enabled, value); }

    public string MinErrors { get => _minErrors; set => SetProperty(ref _minErrors, value ?? string.Empty); }

    public EventLogWatch ToWatch() => new()
    {
        InfobaseId = Infobase.Id,
        Enabled = Enabled,
        MinErrors = int.TryParse(_minErrors, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : 1
    };
}
