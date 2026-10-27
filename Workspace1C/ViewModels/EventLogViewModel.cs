using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Workspace1C.Collections;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services.EventLog;

namespace Workspace1C.ViewModels;

/// <summary>
/// ViewModel окна «Журнал регистрации» (выпуск 1.0.0.69): отбор по периоду,
/// уровням важности, пользователю, событию и тексту; фоновая загрузка с отменой;
/// подробности выбранной записи и экспорт в CSV. Чистый .NET — используется
/// окнами WPF и Avalonia.
/// </summary>
public sealed class EventLogViewModel : ViewModelBase
{
    /// <summary>Варианты лимита записей (индекс <see cref="LimitIndex"/>).</summary>
    public static readonly int[] Limits = { 1000, 5000, 20000, 100000 };

    private readonly IReadOnlyList<Infobase> _infobases;
    private JournalLocation _location;
    private CancellationTokenSource? _cts;
    private IReadOnlyList<EventLogEntry> _entries = Array.Empty<EventLogEntry>();
    private int _selectedIndex = -1;

    private int _periodIndex = 1;
    private int _limitIndex = 1;
    private bool _showInfo = true;
    private bool _showWarning = true;
    private bool _showError = true;
    private bool _showNote = true;
    private string _userFilter = string.Empty;
    private string _eventFilter = string.Empty;
    private string _textFilter = string.Empty;
    private string _statusText = string.Empty;
    private string _detailsText = string.Empty;
    private bool _isBusy;
    private EventLogRowViewModel? _selectedRow;

    /// <param name="title">Заголовок (обычно имя базы).</param>
    /// <param name="location">Расположение журнала.</param>
    public EventLogViewModel(string title, JournalLocation location)
    {
        _infobases = Array.Empty<Infobase>();
        InfobaseNames = Array.Empty<string>();
        Title = title ?? string.Empty;
        _location = location ?? new JournalLocation();
        PeriodOptions = new[]
        {
            LocalizationManager.T("EventLog.Period.Hour"),
            LocalizationManager.T("EventLog.Period.Today"),
            LocalizationManager.T("EventLog.Period.Yesterday"),
            LocalizationManager.T("EventLog.Period.Week"),
            LocalizationManager.T("EventLog.Period.Month"),
            LocalizationManager.T("EventLog.Period.All"),
        };
        LimitOptions = Limits.Select(l => l.ToString("N0", CultureInfo.CurrentCulture)).ToArray();
        StatusText = LocalizationManager.T("EventLog.Ready");
    }

    /// <summary>
    /// Конструктор для открытия из Утилиты (issue #74): список баз для селектора
    /// вверху формы и база, выбранная в Утилите (подставляется автоматически).
    /// </summary>
    /// <param name="infobases">Все базы (для селектора вверху формы).</param>
    /// <param name="selected">База, выбранная в Утилите; null — первая из списка.</param>
    public EventLogViewModel(IEnumerable<Infobase> infobases, Infobase? selected)
    {
        _infobases = (infobases ?? Array.Empty<Infobase>())
            .Where(ib => ib is not null)
            .OrderBy(ib => ib.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        InfobaseNames = _infobases.Select(ib => ib.Name).ToList();
        PeriodOptions = new[]
        {
            LocalizationManager.T("EventLog.Period.Hour"),
            LocalizationManager.T("EventLog.Period.Today"),
            LocalizationManager.T("EventLog.Period.Yesterday"),
            LocalizationManager.T("EventLog.Period.Week"),
            LocalizationManager.T("EventLog.Period.Month"),
            LocalizationManager.T("EventLog.Period.All"),
        };
        LimitOptions = Limits.Select(l => l.ToString("N0", CultureInfo.CurrentCulture)).ToArray();
        StatusText = LocalizationManager.T("EventLog.Ready");
        Title = string.Empty;
        _location = new JournalLocation();

        var index = selected is null ? -1 : _infobases.ToList().FindIndex(ib => ib.Id == selected.Id);
        _selectedIndex = index >= 0 ? index : (_infobases.Count > 0 ? 0 : -1);
        ApplySelectedBase();
    }

    public string Title { get; private set; }

    /// <summary>Имена баз для селектора вверху формы (issue #74).</summary>
    public IReadOnlyList<string> InfobaseNames { get; }

    /// <summary>База, выбранная вверху формы.</summary>
    public Infobase? SelectedInfobase =>
        _selectedIndex >= 0 && _selectedIndex < _infobases.Count ? _infobases[_selectedIndex] : null;

    /// <summary>Индекс выбранной базы в <see cref="InfobaseNames"/>.</summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (SetProperty(ref _selectedIndex, value))
                ApplySelectedBase();
        }
    }

    /// <summary>Описание журнала: формат и каталог.</summary>
    public string LocationText => string.Format(
        LocalizationManager.T("EventLog.LocationFormat"), _location.FormatDisplay, _location.LogDir);

    public JournalLocation Location => _location;

    public IReadOnlyList<string> PeriodOptions { get; }

    public IReadOnlyList<string> LimitOptions { get; }

    public ObservableRangeCollection<EventLogRowViewModel> Rows { get; } = new();

    public int PeriodIndex
    {
        get => _periodIndex;
        set => SetProperty(ref _periodIndex, Math.Clamp(value, 0, 5));
    }

    public int LimitIndex
    {
        get => _limitIndex;
        set => SetProperty(ref _limitIndex, Math.Clamp(value, 0, Limits.Length - 1));
    }

    public bool ShowInfo { get => _showInfo; set => SetProperty(ref _showInfo, value); }

    public bool ShowWarning { get => _showWarning; set => SetProperty(ref _showWarning, value); }

    public bool ShowError { get => _showError; set => SetProperty(ref _showError, value); }

    public bool ShowNote { get => _showNote; set => SetProperty(ref _showNote, value); }

    public string UserFilter { get => _userFilter; set => SetProperty(ref _userFilter, value ?? string.Empty); }

    public string EventFilter { get => _eventFilter; set => SetProperty(ref _eventFilter, value ?? string.Empty); }

    public string TextFilter { get => _textFilter; set => SetProperty(ref _textFilter, value ?? string.Empty); }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string DetailsText
    {
        get => _detailsText;
        private set => SetProperty(ref _detailsText, value);
    }

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

    public EventLogRowViewModel? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (SetProperty(ref _selectedRow, value))
                DetailsText = value is null ? string.Empty : BuildDetails(value.Entry);
        }
    }

    /// <summary>Записи последней выборки (для экспорта).</summary>
    public IReadOnlyList<EventLogEntry> Entries => _entries;

    /// <summary>Подставляет журнал выбранной базы (issue #74).</summary>
    private void ApplySelectedBase()
    {
        var ib = SelectedInfobase;
        if (ib is null)
        {
            Title = string.Empty;
            ChangeLocation(new JournalLocation { ErrorMessage = LocalizationManager.T("EventLog.NoBase") });
        }
        else
        {
            Title = ib.Name;
            ChangeLocation(LgdJournalLocator.Resolve(ib));
        }
        OnPropertyChanged(nameof(Title));
    }

    /// <summary>Меняет журнал (например, выбран другой каталог) и очищает таблицу.</summary>
    public void ChangeLocation(JournalLocation location)
    {
        _location = location ?? new JournalLocation();
        _entries = Array.Empty<EventLogEntry>();
        Rows.ReplaceAll(Array.Empty<EventLogRowViewModel>());
        SelectedRow = null;
        OnPropertyChanged(nameof(Location));
        OnPropertyChanged(nameof(LocationText));
        StatusText = _location.IsValid
            ? LocalizationManager.T("EventLog.Ready")
            : _location.ErrorMessage ?? LocalizationManager.T("EventLog.NotFound");
    }

    /// <summary>Период для пресета: 0 — час, 1 — сегодня, 2 — вчера, 3 — 7 дней, 4 — 30 дней, 5 — всё.</summary>
    public static (DateTime From, DateTime To) ResolvePeriod(int index, DateTime now) => index switch
    {
        0 => (now.AddHours(-1), DateTime.MaxValue),
        1 => (now.Date, DateTime.MaxValue),
        2 => (now.Date.AddDays(-1), now.Date.AddTicks(-1)),
        3 => (now.Date.AddDays(-6), DateTime.MaxValue),
        4 => (now.Date.AddDays(-29), DateTime.MaxValue),
        _ => (DateTime.MinValue, DateTime.MaxValue),
    };

    /// <summary>Условия отбора по текущему состоянию фильтров.</summary>
    public EventLogQuery BuildQuery(DateTime now)
    {
        var (from, to) = ResolvePeriod(PeriodIndex, now);
        return new EventLogQuery
        {
            From = from,
            To = to,
            IncludeInfo = ShowInfo,
            IncludeWarning = ShowWarning,
            IncludeError = ShowError,
            IncludeNote = ShowNote,
            User = UserFilter,
            Event = EventFilter,
            Text = TextFilter,
            MaxRows = Limits[LimitIndex],
        };
    }

    /// <summary>Загружает записи в фоне. Вызывать из UI-потока.</summary>
    public async Task LoadAsync()
    {
        if (IsBusy)
            return;
        if (!_location.IsValid)
        {
            StatusText = _location.ErrorMessage ?? LocalizationManager.T("EventLog.NotFound");
            return;
        }

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var location = _location;
        var query = BuildQuery(DateTime.Now);

        IsBusy = true;
        StatusText = LocalizationManager.T("EventLog.Loading");
        try
        {
            var started = DateTime.Now;
            var result = await Task.Run(() => EventLogQueryService.Query(location, query, token), token);
            _entries = result.Entries;
            Rows.ReplaceAll(result.Entries.Select(e => new EventLogRowViewModel(e)).ToList());
            SelectedRow = null;

            if (result.Error is not null)
            {
                StatusText = string.Format(LocalizationManager.T("EventLog.ReadError"), result.Error);
                return;
            }

            var errors = result.Entries.Count(e => e.Severity == EventLogSeverity.Error);
            var warnings = result.Entries.Count(e => e.Severity == EventLogSeverity.Warning);
            var seconds = (DateTime.Now - started).TotalSeconds;
            StatusText = string.Format(
                LocalizationManager.T(result.Truncated ? "EventLog.StatusTruncated" : "EventLog.Status"),
                result.Entries.Count, errors, warnings, seconds.ToString("0.0", CultureInfo.CurrentCulture));
        }
        catch (OperationCanceledException)
        {
            StatusText = LocalizationManager.T("EventLog.Cancelled");
        }
        catch (Exception ex)
        {
            StatusText = string.Format(LocalizationManager.T("EventLog.ReadError"), ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Прерывает текущую загрузку.</summary>
    public void Cancel()
    {
        try { _cts?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    /// <summary>Экспортирует текущую выборку в CSV; возвращает число записей.</summary>
    public int ExportCsv(string path)
    {
        EventLogCsvExporter.Export(path, _entries);
        return _entries.Count;
    }

    /// <summary>Полное описание записи для панели подробностей.</summary>
    public static string BuildDetails(EventLogEntry e)
    {
        var sb = new StringBuilder();
        void Line(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                sb.Append(LocalizationManager.T(key)).Append(": ").AppendLine(value);
        }

        Line("EventLog.Col.Date", e.Timestamp.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture));
        Line("EventLog.Col.Severity", EventLogPresentation.SeverityTitle(e.Severity));
        Line("EventLog.Col.Event", EventLogPresentation.EventTitle(e.EventName));
        Line("EventLog.Col.User", e.User);
        Line("EventLog.Col.Computer", e.Computer);
        Line("EventLog.Col.Application", EventLogPresentation.ApplicationTitle(e.Application));
        Line("EventLog.Col.Session", e.Session == 0 ? null : e.Session.ToString(CultureInfo.InvariantCulture));
        Line("EventLog.Col.Transaction", EventLogPresentation.TransactionTitle(e.TransactionStatus));
        Line("EventLog.Col.Metadata", string.IsNullOrEmpty(e.MetadataType) || e.MetadataName.Contains('.')
            ? e.MetadataName
            : e.MetadataType + "." + e.MetadataName);
        Line("EventLog.Col.DataPresentation", e.DataPresentation);
        Line("EventLog.Col.Data", e.Data);
        if (!string.IsNullOrWhiteSpace(e.Comment))
        {
            sb.AppendLine();
            sb.AppendLine(LocalizationManager.T("EventLog.Col.Comment") + ":");
            sb.AppendLine(e.Comment);
        }
        return sb.ToString().TrimEnd();
    }
}

/// <summary>Строка таблицы журнала регистрации (готовые к показу тексты).</summary>
public sealed class EventLogRowViewModel
{
    public EventLogRowViewModel(EventLogEntry entry)
    {
        Entry = entry;
        TimeText = entry.Timestamp.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture);
        SeverityText = EventLogPresentation.SeverityTitle(entry.Severity);
        SeverityGlyph = EventLogPresentation.SeverityGlyph(entry.Severity);
        SeverityColor = EventLogPresentation.SeverityColor(entry.Severity);
        EventText = EventLogPresentation.EventTitle(entry.EventName);
        ApplicationText = EventLogPresentation.ApplicationTitle(entry.Application);
        var comment = (entry.Comment ?? string.Empty).Replace("\r", " ").Replace("\n", " ");
        CommentText = comment.Length > 300 ? comment.Substring(0, 300) + "…" : comment;
        MetadataText = entry.MetadataName;
        DataText = string.IsNullOrEmpty(entry.DataPresentation) ? entry.Data : entry.DataPresentation;
    }

    public EventLogEntry Entry { get; }

    public string TimeText { get; }

    public string SeverityText { get; }

    public string SeverityGlyph { get; }

    /// <summary>Цвет уровня #RRGGBB.</summary>
    public string SeverityColor { get; }

    public string User => Entry.User;

    public string Computer => Entry.Computer;

    public string ApplicationText { get; }

    public string EventText { get; }

    public string MetadataText { get; }

    public string DataText { get; }

    public string CommentText { get; }

    public bool IsError => Entry.Severity == EventLogSeverity.Error;
}
