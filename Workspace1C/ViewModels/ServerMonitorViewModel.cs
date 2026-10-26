using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Windows.Input;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// ViewModel окна «Серверы 1С» (0.3.9.124, цикл 0.3.9.123–0.3.9.126): подключение
/// к серверу 1С через rac (адрес/порт/логин/пароль), список кластеров, вкладки
/// «Рабочие процессы / Сеансы / Соединения / Блокировки / Информация о кластере»,
/// ручное обновление, действия (завершение сеанса / разрыв соединения с
/// подтверждением) и автообновление по таймеру 5 с (этап 3, 0.3.9.125).
/// Чистый .NET без платформенных зависимостей — обе платформы (WPF и Avalonia);
/// окна только привязываются.
/// </summary>
public sealed class ServerMonitorViewModel : ViewModelBase, IDisposable
{
    /// <summary>Период автообновления данных кластера, миллисекунды (~5 секунд).</summary>
    public const int AutoRefreshIntervalMs = 5000;

    private readonly IRacClient _rac;
    private readonly IDialogService _dialogs;
    private readonly Action<Action>? _dispatchToUi;
    private readonly IServerListStore? _serverListStore;

    /// <summary>
    /// Показ диалога повтора удаления базы из кластера (issue #69): реализует окно
    /// монитора (WPF/Avalonia), чтобы VM оставалась тестируемой. null — диалог
    /// недоступен (тесты без колбэка): используется прежнее поведение —
    /// предупреждение с одной кнопкой OK.
    /// </summary>
    private readonly Func<string, string?, DropRetryResult>? _dropRetryDialog;

    private Timer? _autoRefreshTimer;
    private int _busy;

    private string _serverAddress = "localhost";
    private int _serverPort = IRacClient.DefaultPort;
    private string _userName = string.Empty;
    private string _password = string.Empty;
    private ManagementServer? _selectedServer;
    private ManagementServer? _connectedServer;
    private Guid? _selectedClusterId;
    private string _statusText = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _hasConnected;
    private RacClusterInfo? _clusterInfo;
    private string _clusterInfoText = string.Empty;
    private RacSessionRow? _selectedSession;
    private RacConnectionRow? _selectedConnection;
    private Guid? _selectedInfobaseId;
    private RacInfobaseRow? _selectedInfobase;

    private ICommand? _connectCommand;
    private ICommand? _refreshCommand;
    private ICommand? _terminateSessionCommand;
    private ICommand? _disconnectConnectionCommand;
    private ICommand? _toggleAutoRefreshCommand;
    private ICommand? _saveClusterPropertiesCommand;
    private ICommand? _dropInfobaseCommand;
    private ICommand? _clearInfobaseFilterCommand;

    /// <summary>Переключатель автообновления (issue #324): включено по умолчанию.</summary>
    private bool _isAutoRefreshEnabled = true;

    /// <summary>Интервал автообновления, секунды (по умолчанию 5; диапазон 1–60).</summary>
    private int _autoRefreshIntervalSeconds = AutoRefreshIntervalMs / 1000;

    /// <summary>
    /// Кэш имён информационных баз выбранного кластера (GUID из «job list» → имя из
    /// «infobase summary list»). Заполняется при загрузке данных кластера; задания без
    /// базы и неизвестные GUID показываются как «—».
    /// </summary>
    private readonly Dictionary<Guid, string> _infobaseNames = new();

    /// <param name="rac">Клиент rac (список кластеров, данные кластера).</param>
    /// <param name="dialogs">Диалоги (сообщения об ошибках; подтверждения действий — этап 3).</param>
    /// <param name="dispatchToUi">
    /// Доставка применения результатов в UI-поток (передаёт окно); null — результаты
    /// применяются прямо из рабочего потока (тесты).
    /// </param>
    /// <param name="serverListStore">Хранилище справочника «Сервера управления» (issue #3);
    /// null — справочник недоступен (тесты без хранилища).</param>
    /// <param name="dropRetryDialog">
    /// Показ диалога повтора удаления базы из кластера (issue #69): получает текст
    /// ошибки и предзаполненный логин администратора ИБ, возвращает решение
    /// пользователя. Реализует окно монитора — чтобы VM оставалась тестируемой;
    /// null — диалог недоступен, при ошибке удаления показывается прежнее
    /// предупреждение с одной кнопкой OK.
    /// </param>
    public ServerMonitorViewModel(
        IRacClient rac,
        IDialogService dialogs,
        Action<Action>? dispatchToUi = null,
        IServerListStore? serverListStore = null,
        Func<string, string?, DropRetryResult>? dropRetryDialog = null)
    {
        _rac = rac ?? throw new ArgumentNullException(nameof(rac));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _dispatchToUi = dispatchToUi;
        _serverListStore = serverListStore;
        _dropRetryDialog = dropRetryDialog;
        LoadServers();
    }

    // ===================== Подключение =====================

    /// <summary>
    /// Адрес сервера 1С (host или IP). Нестандартный порт можно указать полем
    /// <see cref="ServerPort"/> или прямо здесь как «host:port» (например «localhost:27545»,
    /// как в командной строке rac) — порт из адреса имеет приоритет (issue #324).
    /// </summary>
    public string ServerAddress
    {
        get => _serverAddress;
        set => SetProperty(ref _serverAddress, value?.Trim() ?? string.Empty);
    }

    /// <summary>
    /// Порт агента сервера 1С (ragent), по умолчанию 1540; для RAS — 1545.
    /// Некорректные значения (<see cref="IRacClient.DefaultPort"/> при ≤ 0).
    /// Если порт указан в <see cref="ServerAddress"/> как «host:port» — приоритет у него.
    /// </summary>
    public int ServerPort
    {
        get => _serverPort;
        set => SetProperty(ref _serverPort, value > 0 ? value : IRacClient.DefaultPort);
    }

    /// <summary>Логин администратора кластера (пустая строка — без аутентификации).</summary>
    public string UserName
    {
        get => _userName;
        set => SetProperty(ref _userName, value ?? string.Empty);
    }

    /// <summary>
    /// Пароль администратора кластера. Живёт ТОЛЬКО в памяти окна (в тестах сеттер
    /// открытый; в UI пароль передаётся вручную из PasswordBox при подключении),
    /// на диск не сохраняется (см. решения планирования, раздел 5 плана).
    /// </summary>
    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value ?? string.Empty);
    }

    // ===================== Справочник «Сервера управления» (issue #3) =====================

    /// <summary>
    /// Список серверов справочника «Сервера управления» (issue #3): в окне выводится
    /// слева отдельным списком (только <see cref="ManagementServer.Description"/>);
    /// при активации строки выполняется подключение к кластеру. Хранится в файле
    /// <c>server_list.json</c> через <see cref="IServerListStore"/>.
    /// </summary>
    public ObservableCollection<ManagementServer> Servers { get; } = new();

    /// <summary>Выбранный сервер справочника (строка слева).</summary>
    public ManagementServer? SelectedServer
    {
        get => _selectedServer;
        set
        {
            if (SetProperty(ref _selectedServer, value))
            {
                // Доступность «Вверх»/«Вниз» зависит от позиции выбранной строки (issue #3).
                OnPropertyChanged(nameof(CanMoveServerUp));
                OnPropertyChanged(nameof(CanMoveServerDown));
            }
        }
    }

    /// <summary>Можно ли переместить выбранный сервер вверх (есть выбор и не первая строка).</summary>
    public bool CanMoveServerUp => SelectedServer is not null && Servers.IndexOf(SelectedServer) > 0;

    /// <summary>Можно ли переместить выбранный сервер вниз (есть выбор и не последняя строка).</summary>
    public bool CanMoveServerDown
    {
        get
        {
            if (SelectedServer is null || Servers.Count == 0)
                return false;
            var index = Servers.IndexOf(SelectedServer);
            return index >= 0 && index < Servers.Count - 1;
        }
    }

    /// <summary>Перемещает выбранный сервер на одну позицию вверх и сохраняет справочник (issue #3).</summary>
    public void MoveServerUp()
    {
        if (!CanMoveServerUp || _serverListStore is null)
            return;
        var index = Servers.IndexOf(SelectedServer!);
        Servers.Move(index, index - 1);
        _serverListStore.Save(Servers.ToList());
        OnPropertyChanged(nameof(CanMoveServerUp));
        OnPropertyChanged(nameof(CanMoveServerDown));
    }

    /// <summary>Перемещает выбранный сервер на одну позицию вниз и сохраняет справочник (issue #3).</summary>
    public void MoveServerDown()
    {
        if (!CanMoveServerDown || _serverListStore is null)
            return;
        var index = Servers.IndexOf(SelectedServer!);
        Servers.Move(index, index + 1);
        _serverListStore.Save(Servers.ToList());
        OnPropertyChanged(nameof(CanMoveServerUp));
        OnPropertyChanged(nameof(CanMoveServerDown));
    }

    /// <summary>
    /// Запись справочника «Сервера управления», к которой установлено текущее подключение
    /// (issue #3, комментарий 7OH 19.10: строка подключённого сервера подсвечивается
    /// жирным). Устанавливается при успешном подключении: в <see cref="ConnectToServerAsync"/>
    /// — выбранной записью, в <see cref="ConnectAsync"/> — записью, совпадающей по
    /// адресу:порту. Сбрасывается при новом подключении и при ошибке подключения.
    /// </summary>
    public ManagementServer? ConnectedServer
    {
        get => _connectedServer;
        private set
        {
            if (ReferenceEquals(_connectedServer, value))
                return;
            _connectedServer = value;
            OnPropertyChanged(nameof(ConnectedServer));
            OnPropertyChanged(nameof(HasConnectedServer));
        }
    }

    /// <summary>Есть ли активное подключение к записи справочника (для подсветки списка).</summary>
    public bool HasConnectedServer => ConnectedServer is not null;

    /// <summary>Есть ли хотя бы один сервер в справочнике (для подсказки пустого списка).</summary>
    public bool HasServers => Servers.Count > 0;

    /// <summary>Перечитывает список серверов из хранилища (при открытии окна).</summary>
    public void LoadServers()
    {
        Servers.Clear();
        if (_serverListStore is null)
        {
            OnPropertyChanged(nameof(HasServers));
            OnPropertyChanged(nameof(CanMoveServerUp));
            OnPropertyChanged(nameof(CanMoveServerDown));
            return;
        }
        foreach (var server in _serverListStore.Load())
            Servers.Add(server);
        OnPropertyChanged(nameof(HasServers));
        OnPropertyChanged(nameof(CanMoveServerUp));
        OnPropertyChanged(nameof(CanMoveServerDown));
    }

    /// <summary>Добавляет сервер в справочник и сохраняет файл.</summary>
    public void AddServer(ManagementServer server)
    {
        if (server is null || _serverListStore is null)
            return;
        if (string.IsNullOrWhiteSpace(server.Id))
            server.Id = Guid.NewGuid().ToString("N");
        Servers.Add(server);
        _serverListStore.Save(Servers.ToList());
        OnPropertyChanged(nameof(HasServers));
        OnPropertyChanged(nameof(CanMoveServerUp));
        OnPropertyChanged(nameof(CanMoveServerDown));
    }

    /// <summary>Обновляет запись справочника (правка) и сохраняет файл.</summary>
    public void UpdateServer(ManagementServer server)
    {
        if (server is null || _serverListStore is null)
            return;
        var index = Servers.ToList().FindIndex(s => s.Id == server.Id);
        if (index >= 0)
            Servers[index] = server;
        _serverListStore.Save(Servers.ToList());
    }

    /// <summary>Удаляет сервер из справочника и сохраняет файл.</summary>
    public void RemoveServer(ManagementServer server)
    {
        if (server is null || _serverListStore is null)
            return;
        Servers.Remove(server);
        if (ReferenceEquals(SelectedServer, server))
            SelectedServer = null;
        _serverListStore.Save(Servers.ToList());
        OnPropertyChanged(nameof(HasServers));
        OnPropertyChanged(nameof(CanMoveServerUp));
        OnPropertyChanged(nameof(CanMoveServerDown));
    }

    /// <summary>
    /// Ищет запись справочника по точке подключения (issue #3, комментарий 7OH 19.10):
    /// совпадение хоста и порта. Порт из адреса «host:port» приоритетнее поля записи —
    /// та же нормализация, что в <see cref="BuildParams"/> (issue #324). Возвращает null,
    /// если подходящей записи нет (обычное подключение к «сырому» адресу).
    /// </summary>
    private ManagementServer? FindServerByAddress(string address, int port)
    {
        var (host, targetPort) = RacConnectionAddress.Split(address, port);
        foreach (var server in Servers)
        {
            var (serverHost, serverPort) = RacConnectionAddress.Split(server.Address, server.Port);
            if (serverPort == targetPort &&
                string.Equals(serverHost, host, StringComparison.OrdinalIgnoreCase))
            {
                return server;
            }
        }
        return null;
    }

    /// <summary>
    /// Подключение к кластеру выбранного сервера справочника (issue #3): адрес/порт/
    /// логин/пароль берутся из записи, затем выполняется обычное подключение
    /// (<see cref="ConnectAsync"/>). Вызывается окном при активации строки списка.
    /// В статус-строке указывается имя сервера (описание), чтобы было видно,
    /// к какому кластеру идёт подключение.
    /// </summary>
    public async Task ConnectToServerAsync(ManagementServer server)
    {
        if (server is null)
            return;
        ServerAddress = server.Address;
        ServerPort = server.Port;
        UserName = server.User;
        Password = server.Password;

        var (host, port) = RacConnectionAddress.Split(server.Address, server.Port);
        StatusText = string.Format(
            LocalizationManager.T("ServerMonitor.Servers.ConnectingFormat"),
            server.Description, host, port);

        await ConnectAsync();

        if (!HasConnected)
        {
            StatusText = string.Format(
                LocalizationManager.T("ServerMonitor.Servers.ConnectFailedFormat"),
                server.Description);
        }
        else
        {
            // Успех: подсвечиваем именно кликнутую запись справочника (жирным; issue #3,
            // комментарий 7OH 19.10). При совпадении адреса:порта у нескольких записей
            // выигрывает выбранная пользователем (ConnectAsync нашёл бы первую).
            ConnectedServer = server;
        }
    }

    // ===================== Кластеры =====================

    /// <summary>Кластеры сервера (модели из rac «cluster list»).</summary>
    public IReadOnlyList<RacCluster> Clusters { get; private set; } = Array.Empty<RacCluster>();

    /// <summary>Кластеры для выпадающего списка (форматированные строки).</summary>
    public IReadOnlyList<RacClusterRow> ClusterRows { get; private set; } = Array.Empty<RacClusterRow>();

    /// <summary>
    /// Выбранный кластер. При установке значения после подключения запускается
    /// загрузка данных кластера (процессы/сеансы/соединения/блокировки/инфо).
    /// </summary>
    public Guid? SelectedClusterId
    {
        get => _selectedClusterId;
        set
        {
            if (!SetProperty(ref _selectedClusterId, value))
                return;

            // Смена кластера делает фильтр по базе бессмысленным (идентификаторы
            // баз другого кластера) — сбрасываем на «Все базы». Сбрасываем через
            // поля напрямую, чтобы не запускать перезагрузку данных из сеттера
            // выбора базы (данные нового кластера загрузит ниже).
            if (_selectedInfobaseId is not null || _selectedInfobase is not null)
            {
                _selectedInfobase = null;
                _selectedInfobaseId = null;
                OnPropertyChanged(nameof(SelectedInfobase));
                OnPropertyChanged(nameof(SelectedInfobaseId));
                ApplyInfobaseFilter();
            }

            if (value is Guid id && HasConnected)
                _ = LoadClusterDataAsync(id);
        }
    }

    // ===================== Команды =====================

    /// <summary>
    /// «Подключиться» (issue #3): к кластеру выбранного сервера справочника
    /// (адрес/порт/логин/пароль из записи); при отсутствии выбора — обычное
    /// подключение по текущим полям (дефолт — localhost).
    /// </summary>
    public ICommand ConnectCommand =>
        _connectCommand ??= new RelayCommand(async () =>
        {
            if (SelectedServer is { } server)
                await ConnectToServerAsync(server);
            else
                await ConnectAsync();
        });

    /// <summary>«Обновить»: перечитать данные выбранного кластера (после подключения).</summary>
    public ICommand RefreshCommand =>
        _refreshCommand ??= new RelayCommand(Refresh);

    // ===================== Действия =====================

    /// <summary>«Завершить сеанс»: подтверждение → session terminate → обновление списков.</summary>
    public ICommand TerminateSessionCommand =>
        _terminateSessionCommand ??= new RelayCommand(async () => await TerminateSessionAsync());

    /// <summary>«Разорвать соединение»: подтверждение → connection disconnect → обновление списков.</summary>
    public ICommand DisconnectConnectionCommand =>
        _disconnectConnectionCommand ??= new RelayCommand(async () => await DisconnectConnectionAsync());

    // ===================== Вкладки =====================

    /// <summary>Рабочие процессы кластера (rphost/rmngr).</summary>
    public ObservableCollection<RacProcessRow> Processes { get; } = new();

    /// <summary>Сеансы пользователей кластера.</summary>
    public ObservableCollection<RacSessionRow> Sessions { get; } = new();

    /// <summary>Соединения клиентов кластера.</summary>
    public ObservableCollection<RacConnectionRow> Connections { get; } = new();

    /// <summary>Блокировки объектов данных кластера.</summary>
    public ObservableCollection<RacLockRow> Locks { get; } = new();

    /// <summary>Сеансы с учётом фильтра по базе (таблица биндится сюда, issue #3).</summary>
    public ObservableCollection<RacSessionRow> FilteredSessions { get; } = new();

    /// <summary>Соединения с учётом фильтра по базе (таблица биндится сюда, issue #3).</summary>
    public ObservableCollection<RacConnectionRow> FilteredConnections { get; } = new();

    /// <summary>Блокировки с учётом фильтра по базе (таблица биндится сюда, issue #3).</summary>
    public ObservableCollection<RacLockRow> FilteredLocks { get; } = new();

    // ===================== Заголовки вкладок с количеством строк (issue #3) =====================

    /// <summary>Заголовок вкладки «Рабочие процессы» с количеством строк («Рабочие процессы (3)»).</summary>
    public string ProcessesTabText => TabHeader("ServerMonitor.Tabs.Processes", Processes.Count);

    /// <summary>Заголовок вкладки «Сеансы» с количеством показываемых строк («Сеансы (12)»).</summary>
    public string SessionsTabText => TabHeader("ServerMonitor.Tabs.Sessions", FilteredSessions.Count);

    /// <summary>Заголовок вкладки «Соединения» с количеством показываемых строк.</summary>
    public string ConnectionsTabText => TabHeader("ServerMonitor.Tabs.Connections", FilteredConnections.Count);

    /// <summary>Заголовок вкладки «Блокировки» с количеством показываемых строк.</summary>
    public string LocksTabText => TabHeader("ServerMonitor.Tabs.Locks", FilteredLocks.Count);

    /// <summary>Заголовок списка информационных баз с количеством строк («Базы (35)»).</summary>
    public string InfobasesHeaderText => TabHeader("ServerMonitor.Tabs.Infobases", Infobases.Count);

    private static string TabHeader(string key, int count) =>
        string.Format(
            LocalizationManager.T("ServerMonitor.Tabs.CountFormat"),
            LocalizationManager.T(key),
            count);

    /// <summary>
    /// Уведомляет UI об изменении заголовков вкладок (количества строк). Вызывается
    /// после пересборки коллекций и после клиентской фильтрации по базе.
    /// </summary>
    private void RaiseTabHeaderChanges()
    {
        OnPropertyChanged(nameof(ProcessesTabText));
        OnPropertyChanged(nameof(SessionsTabText));
        OnPropertyChanged(nameof(ConnectionsTabText));
        OnPropertyChanged(nameof(LocksTabText));
        OnPropertyChanged(nameof(InfobasesHeaderText));
    }

    /// <summary>
    /// Свойства кластера «свойство — значение» на вкладке «Информация о кластере»
    /// (issue #324, C3): ограниченный набор параметров доступен для правки
    /// (см. <see cref="RacClusterPropertyRow.IsEditable"/>), остальные — только чтение.
    /// </summary>
    public ObservableCollection<RacClusterPropertyRow> ClusterProperties { get; } = new();

    /// <summary>«Сохранить изменения» на вкладке «Информация о кластере» (rac «cluster update»).</summary>
    public ICommand SaveClusterPropertiesCommand =>
        _saveClusterPropertiesCommand ??= new RelayCommand(async () => await SaveClusterPropertiesAsync());

    /// <summary>Информация о кластере (команда «cluster info»); null, если не получена.</summary>
    public RacClusterInfo? ClusterInfo
    {
        get => _clusterInfo;
        private set => SetProperty(ref _clusterInfo, value);
    }

    /// <summary>Текст вкладки «Информация о кластере» («ключ: значение» построчно).</summary>
    public string ClusterInfoText
    {
        get => _clusterInfoText;
        private set => SetProperty(ref _clusterInfoText, value);
    }

    /// <summary>Выбранный сеанс на вкладке «Сеансы» (кнопка «Завершить сеанс»).</summary>
    public RacSessionRow? SelectedSession
    {
        get => _selectedSession;
        set => SetProperty(ref _selectedSession, value);
    }

    /// <summary>Выбранное соединение на вкладке «Соединения» (кнопка «Разорвать соединение»).</summary>
    public RacConnectionRow? SelectedConnection
    {
        get => _selectedConnection;
        set => SetProperty(ref _selectedConnection, value);
    }

    // ===================== Базы (issue #3) =====================

    /// <summary>
    /// Информационные базы выбранного кластера (команда rac «infobase summary list»):
    /// вкладка «Базы». При выборе базы сеансы/соединения/блокировки загружаются
    /// в разрезе базы (параметр rac «--infobase=...»).
    /// </summary>
    public ObservableCollection<RacInfobaseRow> Infobases { get; } = new();

    /// <summary>Есть ли хотя бы одна база в списке (для подсказки пустого списка).</summary>
    public bool HasInfobases => Infobases.Count > 0;

    private bool _infobasesSortDescending;

    /// <summary>
    /// Порядок списка баз (issue #3, 7OH 17.10): базы всегда отсортированы по имени —
    /// по алфавиту (А→Я) или в обратном порядке (Я→А); переключается кнопками над списком.
    /// </summary>
    public bool InfobasesSortDescending
    {
        get => _infobasesSortDescending;
        private set => SetProperty(ref _infobasesSortDescending, value);
    }

    /// <summary>
    /// Сортирует список баз по имени (кнопки «по алфавиту» / «обратная», issue #3).
    /// Выбранная база и фильтр по ней сохраняются; rac не вызывается.
    /// </summary>
    public void SortInfobases(bool descending)
    {
        InfobasesSortDescending = descending;
        var selected = _selectedInfobase;
        var sorted = SortInfobaseRows(Infobases.ToList(), descending);
        ReplaceRows(Infobases, sorted);
        if (selected is not null && Infobases.Contains(selected))
        {
            _selectedInfobase = selected;
            OnPropertyChanged(nameof(SelectedInfobase));
        }
    }

    /// <summary>Чистая сортировка строк баз по имени без учёта регистра (issue #3).</summary>
    internal static IReadOnlyList<RacInfobaseRow> SortInfobaseRows(
        IEnumerable<RacInfobaseRow> rows, bool descending)
    {
        var comparer = StringComparer.Create(System.Globalization.CultureInfo.CurrentCulture, ignoreCase: true);
        var list = rows.ToList();
        return (descending
                ? list.OrderByDescending(r => r.Name ?? string.Empty, comparer)
                : list.OrderBy(r => r.Name ?? string.Empty, comparer))
            .ToList();
    }

    /// <summary>
    /// Выбранная база в списке «Базы»: при изменении сеансы/соединения/блокировки
    /// фильтруются на клиенте по выбранной базе (issue #3). Одновременно обновляется
    /// доступность кнопок «Изменить»/«Удалить» (issue #3: раньше кнопка «Удалить»
    /// оставалась неактивной, потому что <see cref="CanDropInfobase"/> не уведомлял
    /// об изменении выбора).
    /// </summary>
    public RacInfobaseRow? SelectedInfobase
    {
        get => _selectedInfobase;
        set
        {
            if (!SetProperty(ref _selectedInfobase, value))
                return;
            ApplySelectedInfobase(value?.InfobaseId);
            OnPropertyChanged(nameof(CanDropInfobase));
        }
    }

    /// <summary>Идентификатор выбранной базы (null — все базы кластера).</summary>
    public Guid? SelectedInfobaseId
    {
        get => _selectedInfobaseId;
        private set => SetProperty(ref _selectedInfobaseId, value);
    }

    /// <summary>
    /// Применяет выбор базы (issue #3): обновляет идентификатор и фильтрует УЖЕ
    /// загруженный снимок кластера на клиенте (сеансы/соединения/блокировки) —
    /// БЕЗ rac-вызовов и без флага занятости. Параметр rac «--infobase=...» на
    /// платформе пользователя не работает (зависание до CommandTimeout), поэтому
    /// данные кластера всегда запрашиваются целиком, а выбор базы лишь скрывает
    /// строки других баз.
    /// </summary>
    private void ApplySelectedInfobase(Guid? infobaseId)
    {
        if (!SetProperty(ref _selectedInfobaseId, infobaseId))
            return;
        ApplyInfobaseFilter();
    }

    /// <summary>Разрешено ли удаление базы (есть выбор и подключение).</summary>
    public bool CanDropInfobase => SelectedInfobase is not null && HasConnected;

    /// <summary>
    /// Можно ли удалить базу вместе с БД на сервере СУБД (комментарий 7OH от 2026-10-17):
    /// вариант диалога удаления доступен ВСЕМ базам списка кластера. В кластере 1С бывают
    /// только клиент-серверные базы (комментарий 7OH от 2026-10-18 20:24 и 19.10 19:12),
    /// поэтому «--drop-database» предлагается всегда; распознанный тип СУБД (белый список
    /// <see cref="IsClientServerDbms"/>) остаётся только диагностической подписью в диалоге
    /// удаления и НЕ блокирует вариант (до 1.0.0.21 блокировал: на версиях rac с нестандартным
    /// значением «dbms» второй пункт не показывался).
    /// </summary>
    public static bool CanDropDatabaseOnServer(RacInfobaseRow? row) => row is not null;

    /// <summary>
    /// Известные клиент-серверные СУБД rac — «белый список» для команды rac
    /// «infobase drop --drop-database»: ровно значения rac «infobase create --dbms»
    /// (комментарий 7OH от 2026-10-18 20:24). До 1.0.0.14 использовался «анти-FILE»
    /// (не пусто И не FILE): на версиях rac, отдающих для файловой базы кластера
    /// иное значение «dbms», файловая база ошибочно получала вариант «с удалением
    /// на сервере СУБД» (комментарий 7OH 18.10 17:16 — «Как может быть файловая
    /// база в кластере?»). Пустое, «File» и незнакомое значения считаются
    /// неизвестными — вариант «с удалением на сервере СУБД» им не предлагается.
    /// </summary>
    internal static bool IsClientServerDbms(string? dbms) =>
        !string.IsNullOrWhiteSpace(dbms) && ClientServerDbmsValues.Contains(dbms.Trim());

    private static readonly HashSet<string> ClientServerDbmsValues = new(StringComparer.OrdinalIgnoreCase)
    {
        RacDbms.MssqlServer,
        RacDbms.PostgreSql,
        RacDbms.IbmDb2,
        RacDbms.OracleDatabase
    };

    /// <summary>
    /// Создание информационной базы в кластере (issue #3): rac «infobase create» по
    /// полям диалога → перечитывание списков. Ошибка — предупреждение + статус-строка.
    /// </summary>
    public async Task CreateInfobaseAsync(RacInfobaseCreateRequest request)
    {
        if (request is null || !HasConnected || SelectedClusterId is not Guid clusterId)
            return;

        try
        {
            var ok = await _rac.CreateInfobaseAsync(BuildParams(), clusterId, request).ConfigureAwait(false);
            if (!ok)
            {
                var detail = BuildActionError(_rac.LastActionError);
                ShowWarningOnUi(
                    LocalizationManager.T("ServerMonitor.Infobase.CreateFailed") + "\n" + detail,
                    LocalizationManager.T("ServerMonitor.Infobase.CreateTitle"));
                StatusText = detail;
                return;
            }

            StatusText = string.Format(
                LocalizationManager.T("ServerMonitor.Infobase.Status.CreatedFormat"), request.Name);
        }
        catch (Exception ex)
        {
            ShowWarningOnUi(
                LocalizationManager.T("ServerMonitor.Infobase.CreateFailed") + "\n" + BuildErrorMessage(ex),
                LocalizationManager.T("ServerMonitor.Infobase.CreateTitle"));
            StatusText = BuildErrorMessage(ex);
        }
        finally
        {
            Refresh();
        }
    }

    /// <summary>
    /// Удаление выбранной базы (issue #3): rac «infobase drop» → перечитывание списков.
    /// Вопрос и выбор варианта («просто удалить из кластера» / «удалить с удалением БД на
    /// сервере СУБД», комментарий 7OH от 2026-10-17) задаёт диалог удаления в окне —
    /// сюда попадает уже выбранный флаг <paramref name="dropDatabase"/>. При неудаче
    /// (защищённая база, issue #69) показывается диалог повтора с полями «Логин»/
    /// «Пароль» администратора ИБ: «Повторить попытку» выполняет ещё один
    /// «infobase drop» с этими учётными данными (при повторной ошибке — снова тот же
    /// диалог: логин сохраняется, пароль вводится заново, а текст помечается
    /// «Повторная попытка не удалась»), «Отмена» — выход. Типовая ошибка rac
    /// распознаётся (<see cref="ClassifyDropError"/>): если базу занимают активные
    /// сеансы/соединения, диалог предлагает «Завершить сеансы и повторить» — перед
    /// повторным «infobase drop» выполняется «connection disconnect»/«session terminate»
    /// (учётные данные администратора кластера из <see cref="BuildParams"/>, креды ИБ
    /// применяются к самому удалению). Без колбэка диалога (тесты) — прежнее
    /// поведение: предупреждение + статус-строка.
    /// </summary>
    public async Task DropSelectedInfobaseAsync(bool dropDatabase = false)
    {
        var row = SelectedInfobase;
        if (row is null || !HasConnected || SelectedClusterId is not Guid clusterId)
            return;

        // Учётные данные администратора ИБ для повторных попыток (issue #69):
        // первый вызов — без кредов; дальше — из ответа диалога повтора.
        string? infobaseUser = null;
        string? infobasePassword = null;
        var attempts = 0;
        try
        {
            while (true)
            {
                var (ok, detail) = await TryDropInfobaseAsync(
                    row, clusterId, dropDatabase, infobaseUser, infobasePassword).ConfigureAwait(false);
                if (ok)
                {
                    StatusText = string.Format(
                        LocalizationManager.T("ServerMonitor.Infobase.Status.DroppedFormat"), row.Name);
                    return;
                }

                attempts++;
                var answer = AskDropRetryOnUi(detail, infobaseUser, attempts);
                if (!answer.Retry)
                {
                    StatusText = attempts > 1
                        ? string.Format(
                            LocalizationManager.T("ServerMonitor.Infobase.Drop.RetryFailedFormat"), detail)
                        : detail;
                    return;
                }

                if (answer.TerminateSessions)
                {
                    // issue #69: rac не удаляет базу, пока её занимают активные сеансы
                    // (соединения, HTTP-сервисы). Кнопка диалога «Завершить сеансы и
                    // повторить» — явное согласие пользователя на разрыв сеансов.
                    var (termOk, termDetail) = await TerminateInfobaseActivityAsync(
                        row.InfobaseId, clusterId).ConfigureAwait(false);
                    if (!termOk)
                    {
                        ShowWarningOnUi(
                            LocalizationManager.T("ServerMonitor.Infobase.Drop.TerminateFailed")
                            + "\n" + termDetail,
                            LocalizationManager.T("ServerMonitor.Infobase.DropTitle"));
                        StatusText = termDetail;
                        return;
                    }

                    StatusText = termDetail;
                }

                infobaseUser = answer.User;
                infobasePassword = answer.Password;
            }
        }
        finally
        {
            Refresh();
        }
    }

    /// <summary>
    /// Одна попытка «infobase drop» (исходная или повторная с учётными данными
    /// администратора ИБ, issue #69): исключения rac превращаются в текст ошибки,
    /// наружу не пробрасываются — цикл повтора решает, пробовать ли ещё.
    /// </summary>
    private async Task<(bool Ok, string Detail)> TryDropInfobaseAsync(
        RacInfobaseRow row, Guid clusterId, bool dropDatabase, string? infobaseUser, string? infobasePassword)
    {
        try
        {
            var ok = await _rac.DropInfobaseAsync(
                BuildParams(), clusterId, row.InfobaseId, dropDatabase,
                infobaseUser, infobasePassword).ConfigureAwait(false);
            return ok
                ? (true, string.Empty)
                : (false, BuildActionError(_rac.LastActionError));
        }
        catch (Exception ex)
        {
            return (false, BuildErrorMessage(ex));
        }
    }

    /// <summary>
    /// Показ диалога повтора удаления (issue #69) в UI-потоке. Первый показ (после
    /// исходной неудачи) — заголовок «Не удалось удалить информационную базу.» +
    /// детали rac; повторные показы помечаются «Повторная попытка удаления не
    /// удалась: ...», чтобы пользователь видел, что креды применены, но rac снова
    /// вернул ошибку (комментарий 7OH от 2026-10-26). Если колбэк диалога не назначен
    /// (тесты/окно без поддержки) — прежнее поведение: предупреждение с одной кнопкой
    /// OK, повтор не выполняется. Модальное окно открывается через dispatch-маршалер
    /// (как <see cref="ShowWarningOnUi"/>), рабочий поток ждёт результат диалога.
    /// </summary>
    private DropRetryResult AskDropRetryOnUi(string detail, string? login, int attempts)
    {
        // Заголовок диалога повтора: на исходной неудаче — «Не удалось удалить
        // информационную базу.» + детали rac; на повторной — «Повторная попытка
        // удаления не удалась: <детали rac>» (issue #69, комментарий 7OH).
        var message = attempts > 1
            ? string.Format(
                LocalizationManager.T("ServerMonitor.Infobase.Drop.RetryFailedFormat"), detail)
            : LocalizationManager.T("ServerMonitor.Infobase.DropFailed") + "\n" + detail;
        if (_dropRetryDialog is null)
        {
            ShowWarningOnUi(message, LocalizationManager.T("ServerMonitor.Infobase.DropTitle"));
            return DropRetryResult.Cancel;
        }

        if (_dispatchToUi is null)
            return _dropRetryDialog(message, login);

        DropRetryResult result = DropRetryResult.Cancel;
        using var gate = new ManualResetEventSlim(false);
        _dispatchToUi(() =>
        {
            try
            {
                result = _dropRetryDialog(message, login);
            }
            finally
            {
                gate.Set();
            }
        });
        gate.Wait();
        return result;
    }

    /// <summary>
    /// Разрыв активных соединений и завершение сеансов информационной базы перед
    /// повторным «infobase drop» (issue #69): rac не удаляет базу, пока её используют
    /// сеансы/соединения (включая HTTP-сервисы). Порядок — сначала «connection
    /// disconnect», затем «session terminate» (соединения указывают на сеансы).
    /// Учётные данные администратора кластера берутся из <see cref="BuildParams"/>
    /// (как у всех действий уровня кластера); креды администратора ИБ применяются
    /// к самому удалению. Возвращает текст статуса об успехе либо текст ошибки.
    /// </summary>
    private async Task<(bool Ok, string Detail)> TerminateInfobaseActivityAsync(
        Guid infobaseId, Guid clusterId)
    {
        try
        {
            var parameters = BuildParams();
            var connections = await _rac.GetConnectionsAsync(parameters, clusterId, infobaseId)
                .ConfigureAwait(false);
            foreach (var connection in connections)
            {
                if (!await _rac.DisconnectConnectionAsync(parameters, clusterId, connection.Id)
                        .ConfigureAwait(false))
                    return (false, BuildActionError(_rac.LastActionError));
            }

            var sessions = await _rac.GetSessionsAsync(parameters, clusterId, infobaseId)
                .ConfigureAwait(false);
            foreach (var session in sessions)
            {
                if (!await _rac.TerminateSessionAsync(parameters, clusterId, session.Id)
                        .ConfigureAwait(false))
                    return (false, BuildActionError(_rac.LastActionError));
            }

            return (true, string.Format(
                LocalizationManager.T("ServerMonitor.Infobase.Drop.TerminatedFormat"),
                connections.Count, sessions.Count));
        }
        catch (Exception ex)
        {
            return (false, BuildErrorMessage(ex));
        }
    }

    /// <summary>
    /// Тип типовой ошибки rac при «infobase drop» (issue #69): от него зависят
    /// подсказка в диалоге повтора и доступность кнопки «Завершить сеансы и
    /// повторить» (базу занимают активные сеансы/соединения/HTTP-сервисы).
    /// </summary>
    public enum RacDropErrorKind
    {
        /// <summary>Ошибка не распознана — общий текст без подсказки.</summary>
        Unknown,

        /// <summary>Защищённая ИБ / недостаточно прав: нужны креды администратора ИБ
        /// (или администратора кластера, если база не защищена).</summary>
        Authentication,

        /// <summary>Базу используют активные сеансы/соединения (включая HTTP-сервисы):
        /// rac не удаляет занятую базу — перед повтором нужно их завершить.</summary>
        Sessions,

        /// <summary>Ошибка сервера СУБД при «--drop-database»: rac использует учётные
        /// данные СУБД из настроек базы.</summary>
        Database
    }

    /// <summary>
    /// Классифицирует текст ошибки rac при «infobase drop» (issue #69): защищённая
    /// ИБ/права (<see cref="RacDropErrorKind.Authentication"/>), активные сеансы и
    /// соединения (<see cref="RacDropErrorKind.Sessions"/>), сервер СУБД
    /// (<see cref="RacDropErrorKind.Database"/>), прочее — Unknown. Диалог повтора
    /// использует результат для подсказки и кнопки «Завершить сеансы и повторить».
    /// Internal — для юнит-тестов классификации без запуска rac.
    /// </summary>
    internal static RacDropErrorKind ClassifyDropError(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return RacDropErrorKind.Unknown;

        var text = detail.ToLowerInvariant();
        if (ContainsAny(text, DropErrorSessionsMarkers))
            return RacDropErrorKind.Sessions;
        if (ContainsAny(text, DropErrorAuthMarkers))
            return RacDropErrorKind.Authentication;
        if (ContainsAny(text, DropErrorDatabaseMarkers))
            return RacDropErrorKind.Database;
        return RacDropErrorKind.Unknown;
    }

    /// <summary>
    /// Локализованная подсказка для диалога повтора по типу ошибки
    /// (<see cref="ClassifyDropError"/>): Unknown — пустая строка (подсказки нет).
    /// Internal — для юнит-тестов читаемости текстов.
    /// </summary>
    internal static string BuildDropRetryHint(RacDropErrorKind kind) => kind switch
    {
        RacDropErrorKind.Authentication => LocalizationManager.T("ServerMonitor.Infobase.Drop.HintAuth"),
        RacDropErrorKind.Sessions => LocalizationManager.T("ServerMonitor.Infobase.Drop.HintSessions"),
        RacDropErrorKind.Database => LocalizationManager.T("ServerMonitor.Infobase.Drop.HintDatabase"),
        _ => string.Empty
    };

    /// <summary>Маркеры занятости базы в тексте ошибки rac (регистронезависимо).</summary>
    private static readonly string[] DropErrorSessionsMarkers =
    {
        "session", "connection", "сеанс", "соединени", "используется", "используют",
        "used by", "busy", "занят", "http-сервис", "http service", "публикац"
    };

    /// <summary>Маркеры ошибки прав/аутентификации в тексте rac (регистронезависимо).</summary>
    private static readonly string[] DropErrorAuthMarkers =
    {
        "access", "authentication", "auth", "denied", "permission", "rights",
        "authoriz", "доступ", "аутентификац", "авториз", "защищен", "защищён",
        "права", "прав на", "прав доступа", "no rights"
    };

    /// <summary>Маркеры ошибки сервера СУБД в тексте rac (регистронезависимо).</summary>
    private static readonly string[] DropErrorDatabaseMarkers =
    {
        "database", "db user", "db-user", "db pwd", "db-pwd", "субд", "база данных",
        "базаданных", "сервер субд"
    };

    private static bool ContainsAny(string text, IReadOnlyList<string> markers)
    {
        foreach (var marker in markers)
        {
            if (text.IndexOf(marker, StringComparison.Ordinal) >= 0)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Изменение выбранной базы (issue #3, кнопка «Изменить»): rac «infobase update»
    /// по полям диалога → перечитывание списков. Ошибка — предупреждение + статус-строка.
    /// </summary>
    public async Task UpdateSelectedInfobaseAsync(RacInfobaseUpdateRequest request)
    {
        var row = SelectedInfobase;
        if (request is null || row is null || !HasConnected || SelectedClusterId is not Guid clusterId)
            return;

        try
        {
            var ok = await _rac.UpdateInfobaseAsync(
                BuildParams(), clusterId, row.InfobaseId, request).ConfigureAwait(false);
            if (!ok)
            {
                var detail = BuildActionError(_rac.LastActionError);
                ShowWarningOnUi(
                    LocalizationManager.T("ServerMonitor.Infobase.UpdateFailed") + "\n" + detail,
                    LocalizationManager.T("ServerMonitor.Infobase.UpdateTitle"));
                StatusText = detail;
                return;
            }

            StatusText = string.Format(
                LocalizationManager.T("ServerMonitor.Infobase.Status.UpdatedFormat"), request.Name);
        }
        catch (Exception ex)
        {
            ShowWarningOnUi(
                LocalizationManager.T("ServerMonitor.Infobase.UpdateFailed") + "\n" + BuildErrorMessage(ex),
                LocalizationManager.T("ServerMonitor.Infobase.UpdateTitle"));
            StatusText = BuildErrorMessage(ex);
        }
        finally
        {
            Refresh();
        }
    }

    /// <summary>
    /// Полные свойства базы для диалога правки (issue #3, 7OH 17.10): rac «infobase info»
    /// с учётными данными администратора ИБ. Возвращает свойства либо текст ошибки rac
    /// (например, нужна аутентификация администратора ИБ).
    /// </summary>
    public async Task<(RacInfobaseInfo? Info, string Error)> LoadInfobaseInfoAsync(
        Guid infobaseId, string? infobaseUser, string? infobasePassword)
    {
        if (!HasConnected || SelectedClusterId is not Guid clusterId || infobaseId == Guid.Empty)
            return (null, LocalizationManager.T("ServerMonitor.Infobase.Props.NotConnected"));
        try
        {
            var info = await _rac.GetInfobaseInfoAsync(
                BuildParams(), clusterId, infobaseId, infobaseUser, infobasePassword).ConfigureAwait(false);
            return info is null
                ? (null, BuildActionError(_rac.LastActionError))
                : (info, string.Empty);
        }
        catch (Exception ex)
        {
            return (null, BuildErrorMessage(ex));
        }
    }

    /// <summary>
    /// ViewModel диалога «Свойства информационной базы» (issue #3): учётные данные
    /// администратора ИБ ищутся в списке баз приложения (та же база на том же сервере),
    /// загрузка полных свойств — <see cref="LoadInfobaseInfoAsync"/>.
    /// </summary>
    public RacInfobasePropertiesViewModel CreateInfobasePropertiesViewModel(
        RacInfobaseSummary summary, IEnumerable<Infobase>? appInfobases)
    {
        var saved = ClusterInfobaseCredentials.Find(appInfobases, ServerAddress, summary?.Name);
        var infobaseId = summary?.InfobaseId ?? Guid.Empty;
        return new RacInfobasePropertiesViewModel(
            summary ?? new RacInfobaseSummary(),
            (user, password) => LoadInfobaseInfoAsync(infobaseId, user, password),
            saved);
    }

    /// <summary>«Удалить базу»: подтверждение → rac «infobase drop» → обновление.</summary>
    public ICommand DropInfobaseCommand =>
        _dropInfobaseCommand ??= new RelayCommand(async () => await DropSelectedInfobaseAsync());

    /// <summary>
    /// Текст индикатора фильтра по базе (issue #3): «Фильтр по базе: <Имя>» при
    /// активном фильтре; «Без фильтра» — фильтр снят. Надпись и кнопка «Сбросить»
    /// видны ВСЕГДА (в отличие от прежней версии, где панель скрывалась при снятом
    /// фильтре).
    /// </summary>
    public string InfobaseFilterText
    {
        get
        {
            if (_selectedInfobaseId is not Guid id)
                return LocalizationManager.T("ServerMonitor.Infobase.NoFilter");
            var name = _infobaseNames.TryGetValue(id, out var n) ? n : string.Empty;
            return string.Format(
                LocalizationManager.T("ServerMonitor.Infobase.FilterFormat"),
                string.IsNullOrWhiteSpace(name) ? id.ToString() : name);
        }
    }

    /// <summary>Активен ли фильтр по базе (для видимости индикатора и кнопки «Сбросить»).</summary>
    public bool HasInfobaseFilter => _selectedInfobaseId is not null;

    /// <summary>«Сбросить»: снимает фильтр по базе — показываются все базы кластера.</summary>
    public ICommand ClearInfobaseFilterCommand =>
        _clearInfobaseFilterCommand ??= new RelayCommand(ClearInfobaseFilter);

    /// <summary>Снимает фильтр по базе (клиентская операция, без rac-вызовов).</summary>
    public void ClearInfobaseFilter()
    {
        if (_selectedInfobase is null && _selectedInfobaseId is null)
            return;
        _selectedInfobase = null;
        _selectedInfobaseId = null;
        OnPropertyChanged(nameof(SelectedInfobase));
        OnPropertyChanged(nameof(SelectedInfobaseId));
        ApplyInfobaseFilter();
    }

    // ===================== Автообновление =====================

    /// <summary>Запущен ли таймер автообновления (после успешного подключения).</summary>
    public bool AutoRefreshActive => _autoRefreshTimer is not null;

    /// <summary>Переключатель автообновления (issue #324): вкл/выкл на форме монитора.</summary>
    public bool IsAutoRefreshEnabled
    {
        get => _isAutoRefreshEnabled;
        set => SetAutoRefreshEnabled(value);
    }

    /// <summary>
    /// Интервал автообновления, секунды (1–60; по умолчанию 5). Смена значения при работающем
    /// таймере перезапускает его (issue #324, комментарий 17/18: «как отключить Автообновления»).
    /// </summary>
    public int AutoRefreshIntervalSeconds
    {
        get => _autoRefreshIntervalSeconds;
        set
        {
            var clamped = Math.Clamp(value, 1, 60);
            if (!SetProperty(ref _autoRefreshIntervalSeconds, clamped))
                return;
            if (_autoRefreshTimer is not null && IsAutoRefreshEnabled)
            {
                StopAutoRefresh();
                StartAutoRefresh();
            }
        }
    }

    /// <summary>Команда переключения автообновления (вкл/выкл).</summary>
    public ICommand ToggleAutoRefreshCommand =>
        _toggleAutoRefreshCommand ??= new RelayCommand(() => SetAutoRefreshEnabled(!IsAutoRefreshEnabled));

    /// <summary>
    /// Включает/выключает автообновление. Выключение останавливает таймер; включение
    /// запускает его только при установленном подключении (ручное «Обновить» доступно всегда).
    /// </summary>
    public void SetAutoRefreshEnabled(bool enabled)
    {
        if (!SetProperty(ref _isAutoRefreshEnabled, enabled))
            return;

        if (enabled)
        {
            if (HasConnected && _autoRefreshTimer is null)
                StartAutoRefresh();
        }
        else
        {
            StopAutoRefresh();
        }
        // Статусная строка внизу окна различает «выключено переключателем» и
        // «остановлено после ошибки» (issue #324): при переключении она обязана
        // обновиться даже если таймер не стартовал/не останавливался.
        OnPropertyChanged(nameof(AutoRefreshText));
    }

    /// <summary>
    /// Подпись состояния автообновления для статусной строки окна (issue #324):
    /// «вкл (N с)» — таймер реально работает; «выкл» — переключатель снят;
    /// «остановлено (ошибка)» — переключатель включён, но таймер остановлен из-за
    /// сбоя загрузки данных (показывает ФАКТИЧЕСКОЕ состояние, а не положение галки).
    /// </summary>
    public string AutoRefreshText
    {
        get
        {
            if (!IsAutoRefreshEnabled)
                return LocalizationManager.T("ServerMonitor.AutoRefreshOff");
            return AutoRefreshActive
                ? string.Format(LocalizationManager.T("ServerMonitor.AutoRefreshOnFormat"), AutoRefreshIntervalSeconds)
                : LocalizationManager.T("ServerMonitor.AutoRefreshStopped");
        }
    }
    // ===================== Статус =====================

    /// <summary>Строка состояния («Подключение…», «Кластеров: N», ошибки).</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>Подробное сообщение последней ошибки (для статус-строки и диалога).</summary>
    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    /// <summary>Успешно ли установлено подключение (список кластеров получен).</summary>
    public bool HasConnected
    {
        get => _hasConnected;
        private set => SetProperty(ref _hasConnected, value);
    }

    /// <summary>Выполняется ли сейчас запрос (флаг занятости исключает наложение, см. ProcessInspectorViewModel).</summary>
    public bool IsBusy => _busy == 1;

    // ===================== Действия =====================

    /// <summary>
    /// «Подключиться»: получить список кластеров (rac «cluster list»); при успехе —
    /// выбрать первый кластер и загрузить его данные. Ошибки не роняют окно —
    /// пишутся в статус-строку/ErrorMessage.
    /// </summary>
    public async Task ConnectAsync()
    {
        if (!TryEnterBusy())
            return;

        Guid? initialClusterId = null;
        try
        {
            ErrorMessage = string.Empty;
            StatusText = LocalizationManager.T("ServerMonitor.Status.Connecting");
            // Новое подключение сбрасывает подсветку предыдущего сервера справочника
            // (issue #3, комментарий 7OH 19.10): при сбое подключения или обращении к
            // адресу вне справочника жирной не должна оставаться запись, к которой
            // соединение уже не установлено.
            ConnectedServer = null;

            var clusters = await _rac.GetClustersAsync(BuildParams()).ConfigureAwait(false);

            ApplyClusters(clusters);
            HasConnected = true;
            // Подсветка записи справочника, к которой установлено подключение (issue #3,
            // комментарий 7OH 19.10): поиск по совпадению адреса:порта для обычного
            // подключения по полям; ConnectToServerAsync затем уточняет запись.
            ConnectedServer = FindServerByAddress(ServerAddress, ServerPort);
            OnPropertyChanged(nameof(CanDropInfobase));
            StatusText = clusters.Count == 0
                ? LocalizationManager.T("ServerMonitor.Status.NoClusters")
                : string.Format(LocalizationManager.T("ServerMonitor.Status.ConnectedFormat"), clusters.Count);

            // Автообновление запускается только после успешного подключения И при
            // включённом переключателе: галку могли снять ДО подключения — таймер
            // в этом случае не должен стартовать (issue #324, комментарий 7OH).
            if (IsAutoRefreshEnabled)
                StartAutoRefresh();

            // Первый кластер выбираем автоматически. Сам выбор (и запускаемая им
            // загрузка данных) делаем ПОСЛЕ выхода из busy: назначение внутри
            // ConnectAsync молча пропускало LoadClusterDataAsync через TryEnterBusy,
            // и данные приходили только через таймер ~5 с спустя (issue #324:
            // «подключение локально занимает почти 5 секунд»).
            if (SelectedClusterId is null && ClusterRows.Count > 0)
                initialClusterId = ClusterRows[0].Id;
        }
        catch (Exception ex)
        {
            HasConnected = false;
            OnPropertyChanged(nameof(CanDropInfobase));
            // issue #324: в сообщении об ошибке указываем ЦЕЛЕВОЙ адрес:порт, к которому
            // шло подключение (поля могли быть заполнены сохранёнными значениями, и
            // пользователю должно быть видно, куда именно «ушёл» запрос), а не только
            // текст rac, который при повторном нажатии может отличаться.
            var (host, port) = RacConnectionAddress.Split(ServerAddress, ServerPort);
            ErrorMessage = string.Format(
                LocalizationManager.T("ServerMonitor.Status.ConnectFailedDetail"),
                host, port, BuildErrorMessage(ex));
            StatusText = LocalizationManager.T("ServerMonitor.Status.ConnectFailed");
            // Без подключения таймер автообновления не работает.
            StopAutoRefresh();
        }
        finally
        {
            ExitBusy();
        }

        // busy освобождён — запускам первичную загрузку данных выбранного кластера.
        // Важно: ApplyClusters при единственном кластере выбирает его ДО HasConnected=true,
        // и сеттер тогда не запускает загрузку; без этого блока данные приходили бы
        // только от таймера ~5 с спустя (issue #324).
        if (SelectedClusterId is { } clusterId && HasConnected && _busy == 0)
            _ = LoadClusterDataAsync(clusterId);
        else if (initialClusterId is { } initialId)
            SelectedClusterId = initialId;
    }

    /// <summary>
    /// Загружает данные выбранного кластера: процессы, сеансы, соединения, блокировки,
    /// регламентные задания, информационные базы (для имён владельцев заданий) и
    /// «cluster info» (параллельно). Результаты применяются через <see cref="_dispatchToUi"/>
    /// (null — напрямую, тесты). Каждая вкладка загружается ИЗОЛИРОВАННО (issue #324,
    /// лог 7OH от 2026-10-08): сбой ОДНОЙ rac-команды (например, нераспознанная схема
    /// вывода при exit=0 и непустом stdout) больше не проваливает общий
    /// <see cref="Task.WhenAll"/> — остальные вкладки заполняются тем, что удалось
    /// разобрать, а сбойные перечисляются в <see cref="ErrorMessage"/>; автообновление
    /// останавливается, ручное «Обновить» остаётся доступным.
    /// </summary>
    public async Task LoadClusterDataAsync(Guid clusterId, CancellationToken cancellationToken = default)
    {
        if (!TryEnterBusy())
            return;

        try
        {
            ErrorMessage = string.Empty;
            StatusText = LocalizationManager.T("ServerMonitor.Status.Loading");

            var parameters = BuildParams();
            var errors = new List<string>();
            var hasParseError = false;

            // Изолированная загрузка одной вкладки: исключение не роняет общий цикл.
            async Task<IReadOnlyList<T>> LoadTabAsync<T>(
                string tabLabel, Func<Task<IReadOnlyList<T>>> load)
            {
                try
                {
                    return await load().ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (RacOutputParseException ex)
                {
                    hasParseError = true;
                    errors.Add($"{tabLabel}: {BuildErrorMessage(ex)}");
                    return Array.Empty<T>();
                }
                catch (Exception ex)
                {
                    errors.Add($"{tabLabel}: {BuildErrorMessage(ex)}");
                    return Array.Empty<T>();
                }
            }

            async Task<RacClusterInfo?> LoadInfoAsync()
            {
                try
                {
                    return await _rac.GetClusterInfoAsync(parameters, clusterId, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    errors.Add(
                        $"{LocalizationManager.T("ServerMonitor.Tabs.Info")}: {BuildErrorMessage(ex)}");
                    return null;
                }
            }

            var processesTask = LoadTabAsync(
                LocalizationManager.T("ServerMonitor.Tabs.Processes"),
                () => _rac.GetProcessesAsync(parameters, clusterId, cancellationToken));
            // issue #3: сеансы/соединения/блокировки ВСЕГДА запрашиваются целиком
            // (без параметра rac «--infobase=...» — на платформе пользователя он не
            // работает и вызывает зависание до CommandTimeout). Выбор базы фильтрует
            // уже загруженный снимок на клиенте (см. ApplyInfobaseFilter).
            var sessionsTask = LoadTabAsync(
                LocalizationManager.T("ServerMonitor.Tabs.Sessions"),
                () => _rac.GetSessionsAsync(parameters, clusterId, null, cancellationToken));
            var connectionsTask = LoadTabAsync(
                LocalizationManager.T("ServerMonitor.Tabs.Connections"),
                () => _rac.GetConnectionsAsync(parameters, clusterId, null, cancellationToken));
            var locksTask = LoadTabAsync(
                LocalizationManager.T("ServerMonitor.Tabs.Locks"),
                () => _rac.GetLocksAsync(parameters, clusterId, null, cancellationToken));
            var infobasesTask = LoadTabAsync(
                LocalizationManager.T("ServerMonitor.Tabs.Infobases"),
                () => _rac.GetInfobasesAsync(parameters, clusterId, cancellationToken));
            var infoTask = LoadInfoAsync();

            await Task.WhenAll(
                processesTask, sessionsTask, connectionsTask, locksTask, infobasesTask, infoTask)
                .ConfigureAwait(false);

            var processes = await processesTask.ConfigureAwait(false);
            var sessions = await sessionsTask.ConfigureAwait(false);
            var connections = await connectionsTask.ConfigureAwait(false);
            var locks = await locksTask.ConfigureAwait(false);
            var infobases = await infobasesTask.ConfigureAwait(false);
            var info = await infoTask.ConfigureAwait(false);

            ApplyClusterData(processes, sessions, connections, locks, infobases, info);

            if (errors.Count > 0)
            {
                // Часть данных не загружена (issue #324): сбойные вкладки перечислены
                // в ошибке, остальные заполнены. Автообновление останавливается — как
                // и раньше при полной ошибке; ручное «Обновить» остаётся доступным,
                // после успеха таймер возобновится.
                ErrorMessage = LocalizationManager.T("ServerMonitor.Status.PartialLoad") +
                               "\n" + string.Join("\n", errors);
                StatusText = hasParseError
                    ? LocalizationManager.T("ServerMonitor.Status.ParseFailed")
                    : LocalizationManager.T("ServerMonitor.Status.LoadFailed");
                StopAutoRefresh();
            }
            else
            {
                StatusText = string.Format(
                    LocalizationManager.T("ServerMonitor.Status.LoadedFormat"),
                    processes.Count, sessions.Count, connections.Count, locks.Count);

                // issue #324: после УСПЕШНОЙ загрузки автообновление возобновляется, если
                // переключатель включён (например, ручное «Обновить» после ошибки разбора).
                if (IsAutoRefreshEnabled && _autoRefreshTimer is null)
                    StartAutoRefresh();
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = LocalizationManager.T("ServerMonitor.Status.Cancelled");
        }
        catch (Exception ex)
        {
            // issue #324: любая ошибка загрузки останавливает таймер автообновления
            // (бесконечный ретрай каждые 5 с после разрыва соединения не нужен —
            // «после ошибки продолжает пытаться получить данные»). Ручное «Обновить»
            // остаётся доступным; после успешной загрузки таймер возобновляется,
            // если переключатель включён.
            ErrorMessage = BuildErrorMessage(ex);
            StatusText = LocalizationManager.T("ServerMonitor.Status.LoadFailed");
            StopAutoRefresh();
        }
        finally
        {
            ExitBusy();
        }
    }

    /// <summary>«Обновить»: перечитать данные выбранного кластера (без подключения — no-op).</summary>
    public void Refresh()
    {
        if (!HasConnected || SelectedClusterId is not Guid id)
            return;
        _ = LoadClusterDataAsync(id);
    }

    /// <summary>
    /// «Завершить сеанс»: подтверждение (предупреждение о потере несохранённых данных),
    /// команда rac «session terminate», обновление списков; ошибка — предупреждение +
    /// статус-строка. Образец — KillSelected из ProcessInspectorViewModel.
    /// </summary>
    public async Task TerminateSessionAsync()
    {
        var row = SelectedSession;
        if (row is null || !HasConnected || SelectedClusterId is not Guid clusterId)
            return;

        if (!_dialogs.Confirm(
                string.Format(LocalizationManager.T("ServerMonitor.TerminateConfirmFormat"), row.User),
                LocalizationManager.T("ServerMonitor.TerminateTitle")))
            return;

        try
        {
            var ok = await _rac.TerminateSessionAsync(BuildParams(), clusterId, row.Id).ConfigureAwait(false);
            if (!ok)
            {
                var detail = BuildActionError(_rac.LastActionError);
                ShowWarningOnUi(
                    string.Format(LocalizationManager.T("ServerMonitor.TerminateFailedFormat"), row.User) + "\n" + detail,
                    LocalizationManager.T("ServerMonitor.TerminateTitle"));
                StatusText = detail;
                return;
            }

            StatusText = string.Format(
                LocalizationManager.T("ServerMonitor.Status.TerminatedFormat"), row.User);
        }
        catch (Exception ex)
        {
            ShowWarningOnUi(
                string.Format(LocalizationManager.T("ServerMonitor.TerminateFailedFormat"), row.User) + "\n" + BuildErrorMessage(ex),
                LocalizationManager.T("ServerMonitor.TerminateTitle"));
            StatusText = BuildErrorMessage(ex);
        }
        finally
        {
            // После действия списки перечитываются (сеанс мог исчезнуть).
            Refresh();
        }
    }

    /// <summary>
    /// «Разорвать соединение»: подтверждение, команда rac «connection disconnect»,
    /// обновление списков; ошибка — предупреждение + статус-строка.
    /// </summary>
    public async Task DisconnectConnectionAsync()
    {
        var row = SelectedConnection;
        if (row is null || !HasConnected || SelectedClusterId is not Guid clusterId)
            return;

        if (!_dialogs.Confirm(
                string.Format(LocalizationManager.T("ServerMonitor.DisconnectConfirmFormat"), row.Host),
                LocalizationManager.T("ServerMonitor.DisconnectTitle")))
            return;

        try
        {
            var ok = await _rac.DisconnectConnectionAsync(BuildParams(), clusterId, row.Id).ConfigureAwait(false);
            if (!ok)
            {
                var detail = BuildActionError(_rac.LastActionError);
                ShowWarningOnUi(
                    string.Format(LocalizationManager.T("ServerMonitor.DisconnectFailedFormat"), row.Host) + "\n" + detail,
                    LocalizationManager.T("ServerMonitor.DisconnectTitle"));
                StatusText = detail;
                return;
            }

            StatusText = string.Format(
                LocalizationManager.T("ServerMonitor.Status.DisconnectedFormat"), row.Host);
        }
        catch (Exception ex)
        {
            ShowWarningOnUi(
                string.Format(LocalizationManager.T("ServerMonitor.DisconnectFailedFormat"), row.Host) + "\n" + BuildErrorMessage(ex),
                LocalizationManager.T("ServerMonitor.DisconnectTitle"));
            StatusText = BuildErrorMessage(ex);
        }
        finally
        {
            Refresh();
        }
    }

    /// <summary>
    /// Запускает таймер автообновления данных выбранного кластера (5 с). Создаётся при
    /// успешном подключении, если переключатель автообновления включён; повторный запуск —
    /// no-op. Тик идёт через <see cref="Refresh"/> с тем же флагом занятости, что и ручное
    /// «Обновить» (наложение исключено). При выключенном переключателе — no-op: единая
    /// защита всех точек запуска (issue #324, «галка снята, а таймер работает»).
    /// </summary>
    private void StartAutoRefresh()
    {
        if (!IsAutoRefreshEnabled)
            return;
        var intervalMs = AutoRefreshIntervalSeconds * 1000;
        _autoRefreshTimer ??= new Timer(_ => Refresh(), null, intervalMs, intervalMs);
        OnPropertyChanged(nameof(AutoRefreshActive));
        OnPropertyChanged(nameof(AutoRefreshText));
        OnPropertyChanged(nameof(IsAutoRefreshEnabled));
    }

    /// <summary>Останавливает таймер автообновления (Dispose окна, ошибка подключения).</summary>
    private void StopAutoRefresh()
    {
        _autoRefreshTimer?.Dispose();
        _autoRefreshTimer = null;
        OnPropertyChanged(nameof(AutoRefreshActive));
        OnPropertyChanged(nameof(AutoRefreshText));
        OnPropertyChanged(nameof(IsAutoRefreshEnabled));
    }

    /// <inheritdoc />
    /// <summary>Останавливает таймер автообновления (вызывается окном при закрытии).</summary>
    public void Dispose() => StopAutoRefresh();

    /// <summary>
    /// Показывает предупреждение в UI-потоке (issue #3, 1.0.0.22; замечание 7OH 19.10):
    /// async-методы используют <c>ConfigureAwait(false)</c>, поэтому продолжение после
    /// await выполняется в потоке пула, а WPF-окно MaterialMessageWindow можно
    /// создавать только в STA-потоке UI — иначе «Вызывающим потоком должен быть STA».
    /// При отсутствии маршалера (тесты) предупреждение показывается сразу, как раньше.
    /// </summary>
    private void ShowWarningOnUi(string message, string title)
    {
        if (_dispatchToUi is null)
        {
            _dialogs.ShowWarning(message, title);
            return;
        }
        _dispatchToUi(() => _dialogs.ShowWarning(message, title));
    }

    // ===================== Внутреннее =====================

    private RacConnectionParams BuildParams()
    {
        // Порт можно указать полем «Порт» или прямо в адресе как «host:port»
        // (например «localhost:27545» — как в командной строке rac, issue #324):
        // порт из адреса имеет приоритет над значением поля.
        var (host, port) = RacConnectionAddress.Split(ServerAddress, ServerPort);
        return new RacConnectionParams
        {
            Address = host,
            Port = port,
            User = UserName?.Trim() ?? string.Empty,
            Password = Password ?? string.Empty
        };
    }

    private void ApplyClusters(IReadOnlyList<RacCluster> clusters)
    {
        void Apply()
        {
            Clusters = clusters;
            ClusterRows = clusters
                .Where(c => c is not null)
                .Select(c => new RacClusterRow(c))
                .ToList();
            OnPropertyChanged(nameof(Clusters));
            OnPropertyChanged(nameof(ClusterRows));

            // issue #324: единственный кластер выбирается в списке СРАЗУ (до завершения
            // ConnectAsync); при нескольких — выбор не навязывается (прежнее поведение);
            // при отсутствии — сбрасывается.
            if (ClusterRows.Count == 1)
                SelectedClusterId = ClusterRows[0].Id;
            else if (ClusterRows.Count == 0)
                SelectedClusterId = null;
        }

        if (_dispatchToUi is null)
            Apply();
        else
            _dispatchToUi(Apply);
    }

    private void ApplyClusterData(
        IReadOnlyList<RacProcessInfo> processes,
        IReadOnlyList<RacSessionInfo> sessions,
        IReadOnlyList<RacConnectionInfo> connections,
        IReadOnlyList<RacLockInfo> locks,
        IReadOnlyList<RacInfobaseSummary> infobases,
        RacClusterInfo? info)
    {
        void Apply()
        {
            // Выбор сохраняем по идентификатору: после автообновления строка с тем же
            // Id остаётся выбранной, исчезнувшая (завершённый сеанс) — сбрасывается.
            var sessionId = SelectedSession?.Id;
            var connectionId = SelectedConnection?.Id;

            // Кэш имён информационных баз кластера: колонка «База» и фильтр по базе
            // используют имя из «infobase summary list».
            _infobaseNames.Clear();
            foreach (var ib in infobases)
            {
                if (ib.InfobaseId != Guid.Empty && !_infobaseNames.ContainsKey(ib.InfobaseId))
                    _infobaseNames[ib.InfobaseId] = ib.Name;
            }

            ReplaceRows(Processes, processes.Select(p => new RacProcessRow(p)));
            // issue #324, C2: в строку сеанса передаётся имя информационной базы
            // (сопоставление infobase-id из «session list» со «infobase summary list»).
            ReplaceRows(Sessions, sessions.Select(s => new RacSessionRow(s, SessionInfobaseName(s))));
            ReplaceRows(Connections, connections.Select(c => new RacConnectionRow(c)));
            ReplaceRows(Locks, locks.Select(l => new RacLockRow(l)));
            ClusterInfo = info;
            ClusterInfoText = FormatClusterInfo(info);
            ReplaceRows(ClusterProperties, BuildClusterPropertyRows(info));

            // Вкладка «Базы» (issue #3): строки из «infobase summary list». Выбор
            // восстанавливается по Id; если база исчезла (например, удалена) — выбор
            // и фильтр сеансов/соединений/блокировок сбрасываются на «Все базы».
            // Сеттеры не используем: восстановление НЕ должно запускать повторную
            // загрузку данных (иначе цикл «выбор → загрузка → восстановление»).
            var infobaseFilter = _selectedInfobaseId;
            ReplaceRows(Infobases, SortInfobaseRows(
                infobases.Select(ib => new RacInfobaseRow(ib)), InfobasesSortDescending));
            OnPropertyChanged(nameof(HasInfobases));
            if (infobaseFilter is Guid fid2 && !_infobaseNames.ContainsKey(fid2))
                infobaseFilter = null;
            _selectedInfobaseId = infobaseFilter;
            OnPropertyChanged(nameof(SelectedInfobaseId));
            _selectedInfobase = infobaseFilter is Guid iid
                ? Infobases.FirstOrDefault(r => r.InfobaseId == iid)
                : null;
            OnPropertyChanged(nameof(SelectedInfobase));
            OnPropertyChanged(nameof(CanDropInfobase));

            // Клиентская фильтрация по выбранной базе (issue #3): пересобираем
            // отфильтрованные коллекции из полного снимка кластера.
            ApplyInfobaseFilter();

            SelectedSession = sessionId is Guid s ? Sessions.FirstOrDefault(x => x.Id == s) : null;
            SelectedConnection = connectionId is Guid c ? Connections.FirstOrDefault(x => x.Id == c) : null;

            // Заголовки вкладок с количеством строк (issue #3): «Сеансы (12)» и т.д.
            RaiseTabHeaderChanges();
        }

        if (_dispatchToUi is null)
            Apply();
        else
            _dispatchToUi(Apply);
    }

    /// <summary>
    /// Имя информационной базы сеанса из кэша (issue #324, C2: колонка «Информационная
    /// база» на вкладке «Сеансы»); «—» для сеансов без базы/неизвестных GUID.
    /// </summary>
    private string SessionInfobaseName(RacSessionInfo session) =>
        session.InfobaseId is Guid id && _infobaseNames.TryGetValue(id, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : LocalizationManager.T("ServerMonitor.Session.UnknownBase");

    /// <summary>
    /// Пересобирает отфильтрованные коллекции (сеансы/соединения/блокировки) по
    /// выбранной базе (issue #3). Фильтрация КЛИЕНТСКАЯ — по уже загруженному снимку
    /// кластера, без rac-вызовов и без флага занятости: сеансы — по InfobaseId строки,
    /// соединения — через сопоставление SessionId соединения к сеансу (по сеансу
    /// узнаём базу), блокировки — по InfobaseId строки. null — «Все базы».
    /// </summary>
    private void ApplyInfobaseFilter()
    {
        var filter = _selectedInfobaseId;
        if (filter is Guid id)
        {
            ReplaceRows(FilteredSessions, Sessions.Where(s => s.InfobaseId == id));
            var sessionInfobase = Sessions
                .Where(s => s.InfobaseId is not null)
                .ToDictionary(s => s.Id, s => s.InfobaseId!.Value);
            ReplaceRows(
                FilteredConnections,
                Connections.Where(c => sessionInfobase.TryGetValue(c.SessionId, out var ib) && ib == id));
            ReplaceRows(FilteredLocks, Locks.Where(l => l.InfobaseId == id));
        }
        else
        {
            ReplaceRows(FilteredSessions, Sessions);
            ReplaceRows(FilteredConnections, Connections);
            ReplaceRows(FilteredLocks, Locks);
        }

        OnPropertyChanged(nameof(InfobaseFilterText));
        OnPropertyChanged(nameof(HasInfobaseFilter));
        // Количество строк во вкладках сеансов/соединений/блокировок могло измениться
        // после фильтрации по базе (issue #3: «Сеансы (0)» при пустом отборе).
        RaiseTabHeaderChanges();
    }

    private static void ReplaceRows<T>(ObservableCollection<T> target, IEnumerable<T> rows)
    {
        target.Clear();
        foreach (var row in rows)
            target.Add(row);
    }

    /// <summary>
    /// Строки «свойство — значение» для вкладки «Информация о кластере» (issue #324, C3):
    /// редактируется безопасный набор параметров (имя, таймауты/лимиты, уровень
    /// безопасности, пинг, аутентификация); остальные свойства — только чтение.
    /// Ключи rac сопоставляются регистронезависимо с нормализацией дефисов
    /// («expiration-timeout» ≡ «expirationTimeout»).
    /// </summary>
    private static IReadOnlyList<RacClusterPropertyRow> BuildClusterPropertyRows(RacClusterInfo? info)
    {
        var rows = new List<RacClusterPropertyRow>();
        if (info is null)
            return rows;

        foreach (var pair in info.Properties)
            rows.Add(new RacClusterPropertyRow(pair.Key, pair.Value, IsEditableClusterProperty(pair.Key)));

        // Резерв: словарь пуст (не распознан) — типизированные поля как только-читаемые.
        if (rows.Count == 0 && info.Name.Length > 0)
        {
            rows.Add(new RacClusterPropertyRow("name", info.Name, editable: true));
            rows.Add(new RacClusterPropertyRow("hostName", info.HostName, editable: false));
        }

        return rows;
    }

    /// <summary>Ключ свойства rac (регистронезависимо, дефисы нормализуются) в канонической форме.</summary>
    internal static string NormalizeClusterPropertyKey(string rawKey) =>
        rawKey?.Replace("-", string.Empty).Replace("_", string.Empty).Trim().ToLowerInvariant() ?? string.Empty;

    /// <summary>Входит ли свойство кластера в безопасный набор правки (rac «cluster update»).</summary>
    internal static bool IsEditableClusterProperty(string rawKey) =>
        NormalizeClusterPropertyKey(rawKey) switch
        {
            "name" or "expirationtimeout" or "lifetimelimit" or "maxmemorysize" or
            "maxmemorytimelimit" or "securitylevel" or "pingperiod" or "pingtimeout" or
            "maxauthattempts" or "authlockduration" => true,
            _ => false
        };

    /// <summary>
    /// Собирает изменения параметров кластера из правленых строк вкладки
    /// «Информация о кластере» (issue #324, C3). Числовые значения разбираются
    /// инвариантно; при ошибке разбора возвращает null и показывает предупреждение.
    /// </summary>
    private RacClusterUpdate? BuildClusterUpdateFromEdits()
    {
        var update = new RacClusterUpdate();
        foreach (var row in ClusterProperties)
        {
            if (!row.IsChanged)
                continue;

            var value = row.EditValue?.Trim() ?? string.Empty;
            switch (NormalizeClusterPropertyKey(row.RawKey))
            {
                case "name":
                    update.Name = value;
                    break;
                case "expirationtimeout":
                case "lifetimelimit":
                case "maxmemorysize":
                case "maxmemorytimelimit":
                case "pingperiod":
                case "pingtimeout":
                case "authlockduration":
                    if (!TryParseLong(row, value, out var longValue))
                        return null;
                    AssignLong(row.RawKey, longValue);
                    break;
                case "securitylevel":
                case "maxauthattempts":
                    if (!TryParseInt(row, value, out var intValue))
                        return null;
                    AssignInt(row.RawKey, intValue);
                    break;
            }
        }

        return update;

        void AssignLong(string rawKey, long value)
        {
            switch (NormalizeClusterPropertyKey(rawKey))
            {
                case "expirationtimeout": update.ExpirationTimeout = value; break;
                case "lifetimelimit": update.LifetimeLimit = value; break;
                case "maxmemorysize": update.MaxMemorySize = value; break;
                case "maxmemorytimelimit": update.MaxMemoryTimeLimit = value; break;
                case "pingperiod": update.PingPeriod = value; break;
                case "pingtimeout": update.PingTimeout = value; break;
                case "authlockduration": update.AuthLockDuration = value; break;
            }
        }

        void AssignInt(string rawKey, int value)
        {
            switch (NormalizeClusterPropertyKey(rawKey))
            {
                case "securitylevel": update.SecurityLevel = value; break;
                case "maxauthattempts": update.MaxAuthAttempts = value; break;
            }
        }
    }

    /// <summary>Разбор числового значения поля правки (инвариантно) с понятной ошибкой.</summary>
    private bool TryParseLong(RacClusterPropertyRow row, string value, out long result)
    {
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
            return true;

        var message = string.Format(
            LocalizationManager.T("ServerMonitor.Cluster.InvalidNumberFormat"),
            row.DisplayName, value);
        ShowWarningOnUi(message, LocalizationManager.T("ServerMonitor.Cluster.SaveTitle"));
        return false;
    }

    /// <summary>Разбор целочисленного значения поля правки (инвариантно) с понятной ошибкой.</summary>
    private bool TryParseInt(RacClusterPropertyRow row, string value, out int result)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
            return true;

        var message = string.Format(
            LocalizationManager.T("ServerMonitor.Cluster.InvalidNumberFormat"),
            row.DisplayName, value);
        ShowWarningOnUi(message, LocalizationManager.T("ServerMonitor.Cluster.SaveTitle"));
        return false;
    }

    /// <summary>
    /// «Сохранить изменения» на вкладке «Информация о кластере» (issue #324, C3):
    /// подтверждение → rac «cluster update» с ТОЛЬКО изменёнными параметрами →
    /// перечитывание данных кластера. Ошибки не роняют окно.
    /// </summary>
    public async Task SaveClusterPropertiesAsync()
    {
        if (!HasConnected || SelectedClusterId is not Guid clusterId)
            return;

        var update = BuildClusterUpdateFromEdits();
        if (update is null)
            return; // предупреждение о неверном числе уже показано
        if (update.IsEmpty)
        {
            StatusText = LocalizationManager.T("ServerMonitor.Cluster.NothingToSave");
            return;
        }

        if (!_dialogs.Confirm(
                string.Format(
                    LocalizationManager.T("ServerMonitor.Cluster.ConfirmFormat"),
                    CountChanges(update)),
                LocalizationManager.T("ServerMonitor.Cluster.SaveTitle")))
            return;

        try
        {
            var ok = await _rac.UpdateClusterAsync(BuildParams(), clusterId, update).ConfigureAwait(false);
            if (!ok)
            {
                var detail = BuildActionError(_rac.LastActionError);
                ShowWarningOnUi(
                    LocalizationManager.T("ServerMonitor.Cluster.SaveFailedFormat") + "\n" + detail,
                    LocalizationManager.T("ServerMonitor.Cluster.SaveTitle"));
                StatusText = detail;
                return;
            }

            StatusText = LocalizationManager.T("ServerMonitor.Cluster.SavedFormat");
        }
        catch (Exception ex)
        {
            ShowWarningOnUi(
                LocalizationManager.T("ServerMonitor.Cluster.SaveFailedFormat") + "\n" + BuildErrorMessage(ex),
                LocalizationManager.T("ServerMonitor.Cluster.SaveTitle"));
            StatusText = BuildErrorMessage(ex);
        }
        finally
        {
            // После обновления параметры кластера перечитываются (и строки правки
            // пересобираются из фактического вывода rac).
            Refresh();
        }
    }

    /// <summary>Число изменённых параметров для текста подтверждения.</summary>
    private static int CountChanges(RacClusterUpdate update) =>
        (update.Name is null ? 0 : 1) +
        (update.ExpirationTimeout is null ? 0 : 1) +
        (update.LifetimeLimit is null ? 0 : 1) +
        (update.MaxMemorySize is null ? 0 : 1) +
        (update.MaxMemoryTimeLimit is null ? 0 : 1) +
        (update.SecurityLevel is null ? 0 : 1) +
        (update.PingPeriod is null ? 0 : 1) +
        (update.PingTimeout is null ? 0 : 1) +
        (update.MaxAuthAttempts is null ? 0 : 1) +
        (update.AuthLockDuration is null ? 0 : 1);

    private static string FormatClusterInfo(RacClusterInfo? info)
    {
        if (info is null)
            return LocalizationManager.T("ServerMonitor.Empty.Info");

        var sb = new StringBuilder();
        if (info.Properties.Count > 0)
        {
            foreach (var pair in info.Properties)
                sb.AppendLine($"{pair.Key}: {pair.Value}");
        }
        else
        {
            // Резерв: если словарь пуст (не распознан), выводим типизированные поля.
            sb.AppendLine($"name: {info.Name}");
            sb.AppendLine($"hostName: {info.HostName}");
            sb.AppendLine($"port: {info.Port}");
            sb.AppendLine($"expirationTimeout: {info.ExpirationTimeout}");
            sb.AppendLine($"lifetimeLimit: {info.LifetimeLimit}");
            sb.AppendLine($"maxMemorySize: {info.MaxMemorySize}");
            sb.AppendLine($"maxMemoryTimeLimit: {info.MaxMemoryTimeLimit}");
            sb.AppendLine($"securityLevel: {info.SecurityLevel}");
            sb.AppendLine($"sessionIdleTimeout: {info.SessionIdleTimeout}");
            sb.AppendLine($"sessionMaxMemorySize: {info.SessionMaxMemorySize}");
            sb.AppendLine($"sessionMaxTimeLimit: {info.SessionMaxTimeLimit}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string BuildErrorMessage(Exception ex)
    {
        var message = ex.Message;
        return string.IsNullOrWhiteSpace(message)
            ? LocalizationManager.T("ServerMonitor.Errors.Unknown")
            : message;
    }

    /// <summary>Текст ошибки действия rac (из <see cref="IRacClient.LastActionError"/>).</summary>
    private static string BuildActionError(string? lastActionError) =>
        string.IsNullOrWhiteSpace(lastActionError)
            ? LocalizationManager.T("ServerMonitor.Errors.Unknown")
            : lastActionError;

    private bool TryEnterBusy()
    {
        // Один запрос за раз: длительная rac-команда не должна копить очередь (см. ProcessInspectorViewModel:80).
        if (Interlocked.Exchange(ref _busy, 1) == 1)
            return false;
        OnPropertyChanged(nameof(IsBusy));
        return true;
    }

    private void ExitBusy()
    {
        Interlocked.Exchange(ref _busy, 0);
        OnPropertyChanged(nameof(IsBusy));
    }
}