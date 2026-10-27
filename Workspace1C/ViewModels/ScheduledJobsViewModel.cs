using System.Collections.ObjectModel;
using System.Text;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// ViewModel окна «Регламентные задания ИБ» (выпуск 1.0.0.85): задания выбранной
/// базы (расписание, итог последнего запуска), включение/выключение одного задания и
/// массовое выключение/включение всех заданий в отмеченных базах — с исключениями.
/// Чистый .NET — обе платформы; операции выполняются через COM (Windows).
/// </summary>
public sealed class ScheduledJobsViewModel : ViewModelBase
{
    private readonly List<ExtensionTargetRow> _allTargets;
    private readonly StringBuilder _log = new();
    private CancellationTokenSource? _cts;
    private string _searchText = string.Empty;
    private ExtensionTargetRow? _selectedTarget;
    private ScheduledJobRow? _selectedJob;
    private bool _isBusy;
    private string _keepText = string.Empty;
    private string _jobsSummary = string.Empty;

    public ScheduledJobsViewModel(IEnumerable<Infobase> infobases, Infobase? selected)
    {
        _allTargets = (infobases ?? Array.Empty<Infobase>())
            .Where(ib => ib is not null && ib.Connection.Type is ConnectionType.File or ConnectionType.ClientServer)
            .OrderBy(ib => ib.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(ib => new ExtensionTargetRow(ib))
            .ToList();
        foreach (var row in _allTargets)
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ExtensionTargetRow.IsChecked))
                    OnPropertyChanged(nameof(CheckedSummary));
            };
        if (selected is not null)
        {
            var row = _allTargets.FirstOrDefault(r => ReferenceEquals(r.Infobase, selected))
                      ?? _allTargets.FirstOrDefault(r => r.Infobase.Id == selected.Id);
            if (row is not null)
            {
                row.IsChecked = true;
                _selectedTarget = row;
            }
        }
        ApplyFilter();
    }

    public bool IsSupported => OperatingSystem.IsWindows();

    public string PlatformHint => IsSupported
        ? LocalizationManager.T("Jobs.Hint")
        : LocalizationManager.T("ComScript.WindowsOnly");

    public ObservableCollection<ExtensionTargetRow> Targets { get; } = new();

    public ObservableCollection<ScheduledJobRow> Jobs { get; } = new();

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
                ApplyFilter();
        }
    }

    public ExtensionTargetRow? SelectedTarget
    {
        get => _selectedTarget;
        set
        {
            if (SetProperty(ref _selectedTarget, value))
            {
                Jobs.Clear();
                SelectedJob = null;
                JobsSummary = string.Empty;
                OnPropertyChanged(nameof(SelectedBaseText));
            }
        }
    }

    public string SelectedBaseText => _selectedTarget is null
        ? LocalizationManager.T("IbUsers.NoBaseSelected")
        : string.Format(LocalizationManager.T("IbUsers.SelectedBase"), _selectedTarget.Name);

    public ScheduledJobRow? SelectedJob
    {
        get => _selectedJob;
        set => SetProperty(ref _selectedJob, value);
    }

    /// <summary>Имена метаданных заданий, которые не трогать при массовой операции.</summary>
    public string KeepText
    {
        get => _keepText;
        set => SetProperty(ref _keepText, value ?? string.Empty);
    }

    public string JobsSummary
    {
        get => _jobsSummary;
        private set => SetProperty(ref _jobsSummary, value ?? string.Empty);
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

    public string LogText => _log.ToString();

    public string CheckedSummary => string.Format(LocalizationManager.T("IbUsers.CheckedSummary"),
        _allTargets.Count(r => r.IsChecked), _allTargets.Count);

    public IReadOnlyList<ExtensionTargetRow> CheckedTargets => _allTargets.Where(r => r.IsChecked).ToList();

    public void SetAllChecked(bool value)
    {
        foreach (var row in Targets)
            row.IsChecked = value;
    }

    public void Cancel()
    {
        try { _cts?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    public void AppendLog(string message)
    {
        _log.Append(DateTime.Now.ToString("HH:mm:ss")).Append("  ").AppendLine(message);
        OnPropertyChanged(nameof(LogText));
    }

    /// <summary>Подставляет имя метаданных выбранного задания в список исключений.</summary>
    public void AddSelectedToKeep()
    {
        if (_selectedJob is null || string.IsNullOrWhiteSpace(_selectedJob.Job.MetadataName))
            return;
        var names = ScheduledJobsService.ParseNames(KeepText);
        if (!names.Contains(_selectedJob.Job.MetadataName, StringComparer.OrdinalIgnoreCase))
            names.Add(_selectedJob.Job.MetadataName);
        KeepText = string.Join(", ", names);
    }

    private void ApplyFilter()
    {
        Targets.Clear();
        var needle = _searchText.Trim();
        foreach (var row in _allTargets)
        {
            if (needle.Length == 0 || row.Name.IndexOf(needle, StringComparison.CurrentCultureIgnoreCase) >= 0)
                Targets.Add(row);
        }
    }

    private CancellationToken BeginOperation()
    {
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        IsBusy = true;
        return _cts.Token;
    }

    /// <summary>Сводка «всего N, включено M, с ошибкой K».</summary>
    public static string BuildSummary(IReadOnlyCollection<InfobaseScheduledJob> jobs) =>
        string.Format(LocalizationManager.T("Jobs.Summary"), jobs.Count, jobs.Count(j => j.Use),
            jobs.Count(j => !string.IsNullOrWhiteSpace(j.LastError)));

    /// <summary>Читает задания выбранной базы.</summary>
    public async Task RefreshJobsAsync()
    {
        if (IsBusy || _selectedTarget is null)
            return;
        var row = _selectedTarget;
        var token = BeginOperation();
        try
        {
            AppendLog(string.Format(LocalizationManager.T("Jobs.LogListing"), row.Name));
            var result = await ScheduledJobsService.ListAsync(row.Infobase, token);
            Jobs.Clear();
            if (!result.Success)
            {
                AppendLog($"{row.Name}: {result.Error}");
                return;
            }
            foreach (var job in result.Jobs)
                Jobs.Add(new ScheduledJobRow(job));
            JobsSummary = BuildSummary(result.Jobs);
            AppendLog($"{row.Name}: {JobsSummary}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Включает или выключает выбранное задание выбранной базы.</summary>
    public async Task<bool> SetSelectedUseAsync(bool use)
    {
        if (IsBusy || _selectedTarget is null || _selectedJob is null)
            return false;
        var row = _selectedTarget;
        var job = _selectedJob.Job;
        var token = BeginOperation();
        bool ok;
        try
        {
            var result = await ScheduledJobsService.SetUseAsync(row.Infobase, job.Uuid, use, token);
            ok = result.Success && result.Found;
            AppendLog(!result.Success
                ? $"{row.Name}: {result.Error}"
                : !result.Found
                    ? string.Format(LocalizationManager.T("Jobs.LogNotFound"), job.DisplayName)
                    : string.Format(LocalizationManager.T(use ? "Jobs.LogEnabled" : "Jobs.LogDisabled"), job.DisplayName, row.Name));
        }
        finally
        {
            IsBusy = false;
        }
        if (ok)
            await RefreshJobsAsync();
        return ok;
    }

    /// <summary>
    /// Включает/выключает все задания во всех отмеченных базах (кроме исключений).
    /// Возвращает число успешно обработанных баз.
    /// </summary>
    public async Task<int> SetAllInCheckedAsync(bool use)
    {
        if (IsBusy)
            return 0;
        var targets = CheckedTargets;
        if (targets.Count == 0)
        {
            AppendLog(LocalizationManager.T("IbUsers.NoChecked"));
            return 0;
        }
        var keep = ScheduledJobsService.ParseNames(KeepText);
        var token = BeginOperation();
        var ok = 0;
        try
        {
            foreach (var row in targets)
                row.Status = LocalizationManager.T("Extensions.StatusQueued");
            foreach (var row in targets)
            {
                if (token.IsCancellationRequested)
                {
                    row.Status = LocalizationManager.T("Extensions.StatusCancelled");
                    continue;
                }
                row.Status = LocalizationManager.T("Extensions.StatusRunning");
                var result = await ScheduledJobsService.SetAllAsync(row.Infobase, use, keep, token);
                if (result.Success)
                {
                    ok++;
                    row.Status = LocalizationManager.T("Extensions.StatusDone");
                    AppendLog(string.Format(LocalizationManager.T(use ? "Jobs.LogAllEnabled" : "Jobs.LogAllDisabled"),
                        row.Name, result.Changed));
                }
                else
                {
                    row.Status = LocalizationManager.T("Extensions.StatusError");
                    AppendLog($"{row.Name}: {result.Error}");
                }
            }
            AppendLog(string.Format(LocalizationManager.T("IbUsers.LogSummary"), ok, targets.Count));
        }
        finally
        {
            IsBusy = false;
        }
        if (ok > 0 && _selectedTarget is not null && targets.Contains(_selectedTarget))
            await RefreshJobsAsync();
        return ok;
    }
}

/// <summary>Строка таблицы регламентных заданий.</summary>
public sealed class ScheduledJobRow
{
    public ScheduledJobRow(InfobaseScheduledJob job)
    {
        Job = job ?? throw new ArgumentNullException(nameof(job));
    }

    public InfobaseScheduledJob Job { get; }

    public string Name => Job.DisplayName;

    public string MetadataName => Job.MetadataName;

    public string UseText => LocalizationManager.T(Job.Use ? "Jobs.UseOn" : "Jobs.UseOff");

    public string Schedule => Job.Schedule;

    public string LastRun => string.IsNullOrWhiteSpace(Job.LastStart)
        ? Job.LastState
        : string.IsNullOrWhiteSpace(Job.LastState) ? Job.LastStart : $"{Job.LastStart} · {Job.LastState}";

    public string LastError => Job.LastError;
}
