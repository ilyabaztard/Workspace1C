using System.Text;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// ViewModel окна «Публикация на веб-сервере» (выпуск 1.0.0.69): параметры webinst
/// для выбранной базы, публикация, удаление публикации и журнал вывода утилиты.
/// </summary>
public sealed class WebPublishViewModel : ViewModelBase
{
    private static readonly WebServerKind[] Servers = { WebServerKind.Iis, WebServerKind.Apache24, WebServerKind.Apache22 };

    private readonly IReadOnlyList<Infobase> _infobases;
    private readonly StringBuilder _log = new();
    private int _selectedIndex = -1;
    private int _serverIndex;
    private string _virtualDirectory = string.Empty;
    private string _physicalDirectory = string.Empty;
    private string _apacheConfPath = WebPublishService.SuggestApacheConf();
    private string _connectionString = string.Empty;
    private bool _isBusy;

    public WebPublishViewModel(IEnumerable<Infobase> infobases, Infobase? selected)
    {
        _infobases = (infobases ?? Array.Empty<Infobase>())
            .Where(ib => ib is not null && ib.Connection.Type != ConnectionType.WebServer)
            .OrderBy(ib => ib.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        InfobaseNames = _infobases.Select(ib => ib.Name).ToList();
        ServerOptions = new[]
        {
            LocalizationManager.T("WebPublish.Server.Iis"),
            "Apache 2.4",
            "Apache 2.2"
        };
        _serverIndex = OperatingSystem.IsWindows() ? 0 : 1;
        var index = selected is null ? -1 : _infobases.ToList().FindIndex(ib => ib.Id == selected.Id);
        SelectedIndex = index >= 0 ? index : (_infobases.Count > 0 ? 0 : -1);
    }

    public IReadOnlyList<string> InfobaseNames { get; }

    public IReadOnlyList<string> ServerOptions { get; }

    public Infobase? SelectedInfobase =>
        _selectedIndex >= 0 && _selectedIndex < _infobases.Count ? _infobases[_selectedIndex] : null;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (!SetProperty(ref _selectedIndex, value))
                return;
            var ib = SelectedInfobase;
            if (ib is null)
                return;
            VirtualDirectory = WebPublishService.SuggestVirtualDirectory(ib.Name);
            ConnectionString = WebPublishService.ConnectionStringFor(ib);
        }
    }

    public int ServerIndex
    {
        get => _serverIndex;
        set
        {
            if (SetProperty(ref _serverIndex, Math.Clamp(value, 0, Servers.Length - 1)))
            {
                PhysicalDirectory = WebPublishService.SuggestPhysicalDirectory(VirtualDirectory, Servers[_serverIndex]);
                OnPropertyChanged(nameof(IsApache));
            }
        }
    }

    public bool IsApache => Servers[_serverIndex] != WebServerKind.Iis;

    public string VirtualDirectory
    {
        get => _virtualDirectory;
        set
        {
            if (SetProperty(ref _virtualDirectory, value ?? string.Empty))
            {
                PhysicalDirectory = WebPublishService.SuggestPhysicalDirectory(_virtualDirectory, Servers[_serverIndex]);
                OnPropertyChanged(nameof(Url));
            }
        }
    }

    public string PhysicalDirectory { get => _physicalDirectory; set => SetProperty(ref _physicalDirectory, value ?? string.Empty); }

    public string ApacheConfPath { get => _apacheConfPath; set => SetProperty(ref _apacheConfPath, value ?? string.Empty); }

    public string ConnectionString { get => _connectionString; set => SetProperty(ref _connectionString, value ?? string.Empty); }

    public string Url => WebPublishService.LocalUrl(VirtualDirectory);

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

    public WebPublishRequest BuildRequest() => new()
    {
        Server = Servers[_serverIndex],
        VirtualDirectory = VirtualDirectory,
        PhysicalDirectory = PhysicalDirectory,
        ApacheConfPath = ApacheConfPath,
        ConnectionString = ConnectionString
    };

    public Task<bool> PublishAsync() => RunAsync(publish: true);

    public Task<bool> DeleteAsync() => RunAsync(publish: false);

    private async Task<bool> RunAsync(bool publish)
    {
        var ib = SelectedInfobase;
        if (IsBusy || ib is null)
            return false;
        var request = BuildRequest();
        var error = WebPublishService.Validate(request);
        if (error is not null)
        {
            AppendLog(LocalizationManager.T(error));
            return false;
        }

        var args = publish
            ? WebPublishService.BuildPublishArguments(request)
            : WebPublishService.BuildDeleteArguments(request);
        IsBusy = true;
        try
        {
            AppendLog("webinst " + string.Join(" ", args.Select(a => a.Contains(' ') || a.Contains('"') ? "\"" + a.Replace("\"", "\\\"") + "\"" : a)));
            var result = await WebPublishService.RunAsync(ib, args);
            if (!string.IsNullOrWhiteSpace(result.CombinedOutput))
                AppendLog(result.CombinedOutput);
            AppendLog(LocalizationManager.T(result.Success
                ? (publish ? "WebPublish.Published" : "WebPublish.Deleted")
                : "WebPublish.Failed"));
            if (result.Success && publish)
                AppendLog(Url);
            return result.Success;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
