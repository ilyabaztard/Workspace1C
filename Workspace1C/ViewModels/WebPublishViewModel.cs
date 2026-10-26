using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// ViewModel окна «Публикация на веб-сервере» (выпуск 1.0.0.69): параметры webinst
/// для выбранной базы, публикация, удаление публикации и журнал вывода утилиты.
/// С 1.0.0.71 (#63) список веб-серверов дополнен обнаруженными инстансами Apache
/// (служба/конфиг), при выборе инстанса автозаполняются конфигурация и каталог
/// публикации, а пользователь может выбрать конфиг-файл для webinst: основной conf
/// или include-файл.
/// </summary>
public sealed class WebPublishViewModel : ViewModelBase
{
    private readonly record struct ServerOption(WebServerKind Kind, ApacheInstance? Instance);

    private readonly IReadOnlyList<Infobase> _infobases;
    private readonly IReadOnlyList<ServerOption> _servers;
    private readonly StringBuilder _log = new();
    private readonly List<string> _confTargetPaths = new();

    private int _selectedIndex = -1;
    private int _serverIndex;
    private int _confTargetIndex = -1;
    private ApacheInstance? _selectedInstance;
    private string _virtualDirectory = string.Empty;
    private string _physicalDirectory = string.Empty;
    private string _apacheConfPath = string.Empty;
    private string _connectionString = string.Empty;
    private readonly ObservableCollection<string> _confTargets = new();
    private bool _isBusy;

    public WebPublishViewModel(IEnumerable<Infobase> infobases, Infobase? selected,
        IReadOnlyList<ApacheInstance>? apacheInstances = null)
    {
        _infobases = (infobases ?? Array.Empty<Infobase>())
            .Where(ib => ib is not null && ib.Connection.Type != ConnectionType.WebServer)
            .OrderBy(ib => ib.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        InfobaseNames = _infobases.Select(ib => ib.Name).ToList();

        var instances = apacheInstances ?? ApacheInstanceDetector.Detect();
        var servers = new List<ServerOption>();
        if (OperatingSystem.IsWindows())
            servers.Add(new ServerOption(WebServerKind.Iis, null));
        foreach (var instance in instances ?? Array.Empty<ApacheInstance>())
            servers.Add(new ServerOption(instance.Kind, instance));
        // Fallback: инстансы не найдены — остаётся вариант Apache по умолчанию
        // (конфиг/каталог — прежние SuggestApacheConf/SuggestPhysicalDirectory).
        if (servers.All(s => s.Kind == WebServerKind.Iis))
            servers.Add(new ServerOption(WebServerKind.Apache24, null));
        _servers = servers;
        ServerOptions = _servers.Select(DisplayServerOption).ToList();

        _serverIndex = 0;
        ApplyCurrentServerDefaults(initial: true);

        var index = selected is null ? -1 : _infobases.ToList().FindIndex(ib => ib.Id == selected.Id);
        SelectedIndex = index >= 0 ? index : (_infobases.Count > 0 ? 0 : -1);
    }

    public IReadOnlyList<string> InfobaseNames { get; }

    /// <summary>Отображаемые варианты веб-серверов: «IIS» (Windows) + инстансы Apache.</summary>
    public IReadOnlyList<string> ServerOptions { get; }

    /// <summary>Варианты конфиг-файла webinst: основной conf и include-файлы (пусто — не применимо).</summary>
    public ObservableCollection<string> ConfTargets => _confTargets;

    /// <summary>Абсолютные пути конфиг-файлов (для webinst): основной и include-файлы графа.</summary>
    internal IReadOnlyList<string> ConfTargetPaths => _confTargetPaths;

    /// <summary>Выбран ли инстанс Apache (а не IIS или fallback Apache).</summary>
    public bool IsInstanceSelected => _selectedInstance is not null;

    /// <summary>Есть ли выбор конфиг-файла для webinst.</summary>
    public bool HasConfTargets => _confTargets.Count > 0;

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
            if (!SetProperty(ref _serverIndex, Math.Clamp(value, 0, _servers.Count - 1)))
                return;
            ApplyCurrentServerDefaults(initial: false);
        }
    }

    public int ConfTargetIndex
    {
        get => _confTargetIndex;
        set
        {
            if (_servers.Count == 0 || _confTargetPaths.Count == 0)
                value = -1;
            else
                value = Math.Clamp(value, 0, _confTargetPaths.Count - 1);
            if (!SetProperty(ref _confTargetIndex, value))
                return;
            if (_confTargetIndex >= 0 && _confTargetIndex < _confTargetPaths.Count)
                ApacheConfPath = _confTargetPaths[_confTargetIndex];
        }
    }

    public bool IsApache => _servers.Count > 0 && _servers[_serverIndex].Kind != WebServerKind.Iis;

    public string VirtualDirectory
    {
        get => _virtualDirectory;
        set
        {
            if (SetProperty(ref _virtualDirectory, value ?? string.Empty))
            {
                ApplyPhysicalForCurrent();
                OnPropertyChanged(nameof(Url));
            }
        }
    }

    public string PhysicalDirectory { get => _physicalDirectory; set => SetProperty(ref _physicalDirectory, value ?? string.Empty); }

    public string ApacheConfPath { get => _apacheConfPath; set => SetProperty(ref _apacheConfPath, value ?? string.Empty); }

    public string ConnectionString { get => _connectionString; set => SetProperty(ref _connectionString, value ?? string.Empty); }

    /// <summary>Порт слушателя выбранного инстанса Apache (LISTEN); null — 80.</summary>
    private int? ListenPort => _selectedInstance?.ListenPorts.FirstOrDefault();

    /// <summary>Адрес опубликованной базы (для Apache учитывается порт LISTEN — issue #63).</summary>
    public string Url => WebPublishService.LocalUrl(VirtualDirectory, ListenPort);

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
        Server = _servers[_serverIndex].Kind,
        VirtualDirectory = VirtualDirectory,
        PhysicalDirectory = PhysicalDirectory,
        ApacheConfPath = ApacheConfPath,
        ConnectionString = ConnectionString
    };

    public Task<bool> PublishAsync() => RunAsync(publish: true);

    public Task<bool> DeleteAsync() => RunAsync(publish: false);

    /// <summary>Показывает, выбран ли именно обнаруженный инстанс Apache (для внутренних тестов).</summary>
    internal ApacheInstance? SelectedApacheInstance => _selectedInstance;

    /// <summary>Индекс пункта в ServerOptions для конкретного инстанса (-1 — не найден).</summary>
    internal int ServerIndexFor(ApacheInstance instance)
    {
        for (var i = 0; i < _servers.Count; i++)
        {
            if (ReferenceEquals(_servers[i].Instance, instance))
                return i;
        }
        return -1;
    }

    private string DisplayServerOption(ServerOption option)
    {
        if (option.Kind == WebServerKind.Iis)
            return LocalizationManager.T("WebPublish.Server.Iis");

        var version = option.Kind == WebServerKind.Apache22 ? "2.2" : "2.4";
        if (option.Instance is { } instance && !string.IsNullOrWhiteSpace(instance.Name))
        {
            // Issue #63: имя конфиг-файла в подписи инстанса — при нескольких службах
            // Apache (httpd.conf / conf\httpd27.conf) видно, что значения по умолчанию
            // берутся из конфига конкретной службы, а не из «обычного» httpd.conf.
            if (!string.IsNullOrWhiteSpace(instance.ConfPath))
            {
                return string.Format(LocalizationManager.T("WebPublish.ApacheInstanceOptionWithConf"),
                    version, instance.Name, Path.GetFileName(instance.ConfPath));
            }
            return string.Format(LocalizationManager.T("WebPublish.ApacheInstanceOption"), version, instance.Name);
        }
        return string.Format(LocalizationManager.T("WebPublish.ApacheDefaultOption"), version);
    }

    private void ApplyCurrentServerDefaults(bool initial)
    {
        if (_servers.Count == 0)
            return;

        var option = _servers[_serverIndex];
        _selectedInstance = option.Instance;

        if (option.Instance is { } instance)
        {
            BuildConfTargets(instance);
            // Каталог публикации — htdocs инстанса; имя виртуального каталога добавляется позже.
            RebuildPhysicalForCurrent();
        }
        else
        {
            _confTargetPaths.Clear();
            _confTargets.Clear();
            _confTargetIndex = -1;
            _apacheConfPath = WebPublishService.SuggestApacheConf();
            RebuildPhysicalForCurrent();
        }

        OnPropertyChanged(nameof(ConfTargets));
        OnPropertyChanged(nameof(ConfTargetIndex));
        OnPropertyChanged(nameof(HasConfTargets));
        OnPropertyChanged(nameof(IsInstanceSelected));
        OnPropertyChanged(nameof(IsApache));
        OnPropertyChanged(nameof(PhysicalDirectory));
        OnPropertyChanged(nameof(ApacheConfPath));
        OnPropertyChanged(nameof(Url));

        _ = initial;
    }

    private void BuildConfTargets(ApacheInstance instance)
    {
        _confTargetPaths.Clear();
        _confTargets.Clear();

        // Файлы для webinst: основной конфиг службы (из -f) и включаемые файлы графа
        // Include (в т.ч. файл алиасов 1c8Aliases*.conf) — issue #63.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(instance.ConfPath))
        {
            seen.Add(NormalizeTargetKey(instance.ConfPath));
            _confTargetPaths.Add(instance.ConfPath);
            _confTargets.Add(string.Format(
                LocalizationManager.T("WebPublish.ConfTargetMain"),
                Path.GetFileName(instance.ConfPath)));
        }
        foreach (var include in instance.IncludeFiles ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(include))
                continue;
            if (!seen.Add(NormalizeTargetKey(include)))
                continue;
            _confTargetPaths.Add(include);
            _confTargets.Add(string.Format(
                LocalizationManager.T("WebPublish.ConfTargetInclude"),
                Path.GetFileName(include)));
        }

        // Fallback (issue #63, «файл алиаса по-прежнему не вернулся»): файл алиасов
        // 1c8Aliases*.conf может подключаться через IncludeOptional вне графа Include или
        // маску каталога (conf\aliases\*.conf), которую не удалось раскрыть. Если его нет
        // в списке — ищем рядом с основным конфигом службы (conf\1c8Aliases*.conf,
        // conf\aliases\1c8Aliases*.conf и т.п.), чтобы пункт «Include: 1c8Aliases27.conf»
        // всегда был доступен для webinst.
        if (!HasAliasFile(_confTargetPaths))
        {
            foreach (var alias in FindAliasFilesNear(instance.ConfPath))
            {
                if (!seen.Add(NormalizeTargetKey(alias)))
                    continue;
                _confTargetPaths.Add(alias);
                _confTargets.Add(string.Format(
                    LocalizationManager.T("WebPublish.ConfTargetInclude"),
                    Path.GetFileName(alias)));
            }
        }

        _confTargetIndex = _confTargetPaths.Count > 0 ? 0 : -1;
        _apacheConfPath = _confTargetPaths.Count > 0 ? _confTargetPaths[0] : string.Empty;
    }

    /// <summary>Есть ли среди целей webinst файл алиасов 1С (1c8Aliases*.conf).</summary>
    private static bool HasAliasFile(IReadOnlyList<string> paths)
        => paths.Any(IsAliasFileName);

    /// <summary>Имя файла алиасов 1С: «1c8Aliases27.conf» и т.п.</summary>
    private static bool IsAliasFileName(string? path)
        => !string.IsNullOrWhiteSpace(path)
           && Path.GetFileName(path).StartsWith("1c8Aliases", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Существующие файлы алиасов (1c8Aliases*.conf) рядом с конфигом службы: каталог
    /// конфига, conf\aliases, ServerRoot\conf\aliases и ServerRoot\aliases. Только реально
    /// существующие файлы; пути с неразрешёнными ${...} пропускаются; дубликаты отсекаются
    /// вызывающим кодом (seen).
    /// </summary>
    private static IEnumerable<string> FindAliasFilesNear(string? confPath)
    {
        if (string.IsNullOrWhiteSpace(confPath))
            yield break;
        string? confDir;
        try
        {
            confDir = Path.GetDirectoryName(confPath);
        }
        catch
        {
            yield break;
        }
        if (string.IsNullOrWhiteSpace(confDir))
            yield break;

        var dirs = new List<string> { confDir };
        try
        {
            dirs.Add(Path.Combine(confDir, "aliases"));
            var serverRoot = Path.GetDirectoryName(confDir);
            if (!string.IsNullOrWhiteSpace(serverRoot))
            {
                dirs.Add(Path.Combine(serverRoot, "conf", "aliases"));
                dirs.Add(Path.Combine(serverRoot, "aliases"));
            }
        }
        catch
        {
            // Невалидный путь — используем только каталог конфига.
        }

        foreach (var dir in dirs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string[] files;
            try
            {
                if (!Directory.Exists(dir))
                    continue;
                files = Directory.GetFiles(dir, "1c8Aliases*.conf");
            }
            catch
            {
                continue;
            }
            foreach (var file in files)
            {
                if (file.IndexOf("${", StringComparison.Ordinal) >= 0)
                    continue;
                yield return file;
            }
        }
    }

    /// <summary>Ключ сравнения путей конфиг-файлов: без учёта регистра и разделителей пути.</summary>
    private static string NormalizeTargetKey(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;
        return path.Trim()
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar)
            .ToUpperInvariant();
    }

    private void ApplyPhysicalForCurrent() => RebuildPhysicalForCurrent();

    private void RebuildPhysicalForCurrent()
    {
        if (_servers.Count == 0)
            return;
        var option = _servers[_serverIndex];
        if (option.Instance is { } instance)
        {
            // DocumentRoot из конфига (issue #63): точный каталог публикации инстанса
            // (включает подкаталог сайта — «ServerRoot\htdocs\27»); fallback — Htdocs.
            var root = !string.IsNullOrWhiteSpace(instance.DocumentRoot) ? instance.DocumentRoot : instance.Htdocs;
            if (!string.IsNullOrWhiteSpace(root))
                PhysicalDirectory = Path.Combine(root, VirtualDirectory);
            else
                PhysicalDirectory = WebPublishService.SuggestPhysicalDirectory(VirtualDirectory, option.Kind);
        }
        else
            PhysicalDirectory = WebPublishService.SuggestPhysicalDirectory(VirtualDirectory, option.Kind);
    }

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
