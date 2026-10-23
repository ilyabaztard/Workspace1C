using System.Globalization;
using Workspace1C.Localization;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// ViewModel окна «Технологический журнал» (выпуск 1.0.0.69): выбор шаблона,
/// каталога логов, срока хранения и порога длительности; предпросмотр XML,
/// включение и отключение журнала в каталоге conf платформы.
/// </summary>
public sealed class TechLogViewModel : ViewModelBase
{
    private static readonly TechLogTemplate[] Templates =
    {
        TechLogTemplate.Exceptions, TechLogTemplate.Locks, TechLogTemplate.SlowQueries, TechLogTemplate.SlowCalls
    };

    private int _templateIndex;
    private string _confDirectory;
    private string _logDirectory;
    private string _historyText = "24";
    private string _thresholdText = "1000";
    private string _statusText = string.Empty;

    public TechLogViewModel()
    {
        ConfDirectories = TechLogService.DetectConfDirectories();
        _confDirectory = ConfDirectories.FirstOrDefault() ?? string.Empty;
        _logDirectory = TechLogService.DefaultLogDirectory();
        TemplateOptions = new[]
        {
            LocalizationManager.T("TechLog.Template.Exceptions"),
            LocalizationManager.T("TechLog.Template.Locks"),
            LocalizationManager.T("TechLog.Template.SlowQueries"),
            LocalizationManager.T("TechLog.Template.SlowCalls"),
        };
        RefreshStatus();
    }

    public IReadOnlyList<string> TemplateOptions { get; }

    public IReadOnlyList<string> ConfDirectories { get; }

    public int TemplateIndex
    {
        get => _templateIndex;
        set
        {
            if (SetProperty(ref _templateIndex, Math.Clamp(value, 0, Templates.Length - 1)))
                OnPropertyChanged(nameof(PreviewXml));
        }
    }

    public string ConfDirectory
    {
        get => _confDirectory;
        set
        {
            if (SetProperty(ref _confDirectory, value ?? string.Empty))
                RefreshStatus();
        }
    }

    public string LogDirectory
    {
        get => _logDirectory;
        set
        {
            if (SetProperty(ref _logDirectory, value ?? string.Empty))
                OnPropertyChanged(nameof(PreviewXml));
        }
    }

    public string HistoryText
    {
        get => _historyText;
        set
        {
            if (SetProperty(ref _historyText, value ?? string.Empty))
                OnPropertyChanged(nameof(PreviewXml));
        }
    }

    public string ThresholdText
    {
        get => _thresholdText;
        set
        {
            if (SetProperty(ref _thresholdText, value ?? string.Empty))
                OnPropertyChanged(nameof(PreviewXml));
        }
    }

    public TechLogTemplate SelectedTemplate => Templates[_templateIndex];

    public int HistoryHours => int.TryParse(_historyText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var h) ? h : 24;

    public int ThresholdMs => int.TryParse(_thresholdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var t) ? t : 1000;

    public string PreviewXml => TechLogService.BuildXml(SelectedTemplate, LogDirectory, HistoryHours, ThresholdMs);

    public string CurrentXml => string.IsNullOrWhiteSpace(_confDirectory)
        ? string.Empty
        : TechLogService.ReadCurrent(_confDirectory);

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public void RefreshStatus()
    {
        var current = CurrentXml;
        var size = TechLogService.DirectorySize(LogDirectory);
        StatusText = string.IsNullOrWhiteSpace(_confDirectory)
            ? LocalizationManager.T("TechLog.NoConf")
            : string.Format(LocalizationManager.T(string.IsNullOrWhiteSpace(current) ? "TechLog.StatusOff" : "TechLog.StatusOn"),
                _confDirectory, (size / 1024d / 1024d).ToString("0.0", CultureInfo.CurrentCulture));
        OnPropertyChanged(nameof(CurrentXml));
    }

    /// <summary>Проверка формы: ключ локализации ошибки или null.</summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(ConfDirectory))
            return "TechLog.NoConf";
        if (string.IsNullOrWhiteSpace(LogDirectory))
            return "TechLog.LogDirRequired";
        if (HistoryHours <= 0 || ThresholdMs <= 0)
            return "TechLog.BadNumbers";
        return null;
    }

    /// <summary>Включает журнал; возвращает текст ошибки или null.</summary>
    public string? Enable()
    {
        var validation = Validate();
        if (validation is not null)
            return LocalizationManager.T(validation);
        var error = TechLogService.Enable(ConfDirectory, PreviewXml);
        RefreshStatus();
        return error;
    }

    /// <summary>Отключает журнал; возвращает текст ошибки или null.</summary>
    public string? Disable()
    {
        if (string.IsNullOrWhiteSpace(ConfDirectory))
            return LocalizationManager.T("TechLog.NoConf");
        var error = TechLogService.Disable(ConfDirectory);
        RefreshStatus();
        return error;
    }
}
