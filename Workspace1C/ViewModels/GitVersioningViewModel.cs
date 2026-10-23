using System.Text;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// ViewModel окна «Версионирование конфигурации в Git» (выпуск 1.0.0.69): настройки
/// репозитория для выбранной базы, ручной запуск выгрузки с коммитом и журнал.
/// Те же настройки использует задание по расписанию «Выгрузить конфигурацию в Git».
/// </summary>
public sealed class GitVersioningViewModel : ViewModelBase
{
    private readonly IReadOnlyList<Infobase> _infobases;
    private readonly GitVersioningData _data;
    private readonly StringBuilder _log = new();
    private CancellationTokenSource? _cts;
    private int _selectedIndex = -1;
    private string _repoPath = string.Empty;
    private string _subFolder = "src";
    private string _messageTemplate = new GitRepoSettings().MessageTemplate;
    private bool _push;
    private bool _isBusy;
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;

    public GitVersioningViewModel(IEnumerable<Infobase> infobases, Infobase? selected, GitVersioningData? data = null)
    {
        _infobases = (infobases ?? Array.Empty<Infobase>())
            .Where(ib => ib is not null)
            .OrderBy(ib => ib.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        _data = data ?? GitVersioningService.Load();
        InfobaseNames = _infobases.Select(ib => ib.Name).ToList();
        var index = selected is null ? -1 : _infobases.ToList().FindIndex(ib => ib.Id == selected.Id);
        SelectedIndex = index >= 0 ? index : (_infobases.Count > 0 ? 0 : -1);
    }

    public IReadOnlyList<string> InfobaseNames { get; }

    public Infobase? SelectedInfobase =>
        _selectedIndex >= 0 && _selectedIndex < _infobases.Count ? _infobases[_selectedIndex] : null;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (!SetProperty(ref _selectedIndex, value))
                return;
            var settings = _data.Find(SelectedInfobase?.Id);
            RepoPath = settings?.RepoPath ?? string.Empty;
            SubFolder = settings?.SubFolder ?? "src";
            MessageTemplate = settings?.MessageTemplate ?? new GitRepoSettings().MessageTemplate;
            Push = settings?.Push ?? false;
            OnPropertyChanged(nameof(IsConfigured));
        }
    }

    public string RepoPath { get => _repoPath; set => SetProperty(ref _repoPath, value ?? string.Empty); }

    public string SubFolder { get => _subFolder; set => SetProperty(ref _subFolder, value ?? string.Empty); }

    public string MessageTemplate { get => _messageTemplate; set => SetProperty(ref _messageTemplate, value ?? string.Empty); }

    public bool Push { get => _push; set => SetProperty(ref _push, value); }

    public bool IsConfigured => _data.Find(SelectedInfobase?.Id) is not null;

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

    /// <summary>
    /// Добавляет строку в журнал окна. Сервис вызывает её из фонового потока —
    /// уведомление переносится в UI-поток (Avalonia не допускает обновление
    /// привязок из чужого потока).
    /// </summary>
    public void AppendLog(string message)
    {
        if (_ui is not null && SynchronizationContext.Current != _ui)
        {
            _ui.Post(_ => AppendLog(message), null);
            return;
        }
        _log.Append(DateTime.Now.ToString("HH:mm:ss")).Append("  ").AppendLine(message);
        OnPropertyChanged(nameof(LogText));
    }

    /// <summary>Текущие настройки формы для выбранной базы.</summary>
    public GitRepoSettings? CurrentSettings() => SelectedInfobase is null
        ? null
        : new GitRepoSettings
        {
            InfobaseId = SelectedInfobase.Id,
            RepoPath = RepoPath.Trim(),
            SubFolder = string.IsNullOrWhiteSpace(SubFolder) ? "src" : SubFolder.Trim(),
            MessageTemplate = MessageTemplate,
            Push = Push
        };

    /// <summary>Проверка формы: ключ локализации ошибки или null.</summary>
    public string? Validate()
    {
        if (SelectedInfobase is null)
            return "Git.SelectBase";
        if (string.IsNullOrWhiteSpace(RepoPath))
            return "Git.RepoRequired";
        try
        {
            GitVersioningService.ResolveDumpDirectory(CurrentSettings()!);
        }
        catch
        {
            return "Git.BadSubFolder";
        }
        return null;
    }

    /// <summary>Сохраняет настройки выбранной базы.</summary>
    public bool Save()
    {
        var error = Validate();
        if (error is not null)
        {
            AppendLog(LocalizationManager.T(error));
            return false;
        }
        _data.Upsert(CurrentSettings()!);
        GitVersioningService.Save(_data);
        OnPropertyChanged(nameof(IsConfigured));
        AppendLog(LocalizationManager.T("Git.Saved"));
        return true;
    }

    public void Cancel()
    {
        try { _cts?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    /// <summary>Сохраняет настройки и выполняет выгрузку с коммитом.</summary>
    public async Task RunAsync()
    {
        if (IsBusy || !Save())
            return;
        var infobase = SelectedInfobase!;
        var settings = CurrentSettings()!;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        IsBusy = true;
        try
        {
            var result = await GitVersioningService.RunAsync(infobase, settings, AppendLog, _cts.Token);
            AppendLog(result.Message);
        }
        catch (OperationCanceledException)
        {
            AppendLog(LocalizationManager.T("Designer.Cancelled"));
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
