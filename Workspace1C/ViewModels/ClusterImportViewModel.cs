using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Windows.Input;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// ViewModel окна «Импорт из кластера 1С» (0.3.9.173, цикл 0.3.9.172–0.3.9.175):
/// подключение к агенту сервера/серверу администрирования через rac (адрес/порт/логин/
/// пароль), список кластеров, загрузка информационных баз выбранного кластера в чеклист
/// (<see cref="ClusterImportRow"/>) с пометкой дубликатов (по строке подключения), сводка
/// перед подтверждением и команда «Импортировать» (возвращает отмеченные новые базы через
/// <see cref="SelectedBases"/>). Файловых баз в кластере не бывает (комментарий 7OH от
/// 2026-10-18 20:24). Чистый .NET без платформенных зависимостей — обе платформы
/// (WPF и Avalonia); окна только привязываются.
/// </summary>
public sealed class ClusterImportViewModel : ViewModelBase
{
    private readonly IRacClient _rac;
    private readonly IReadOnlyList<Infobase> _existingInfobases;
    private readonly Action<Action>? _dispatchToUi;
    private readonly Dictionary<Guid, RacClusterInfo?> _clusterInfoCache = new();
    /// <summary>Логгер (nullable — в тестах DI может отсутствовать).</summary>
    private readonly IAppLogger? _logger;

    private int _busy;
    private string _serverAddress = "localhost";
    private int _serverPort = IRacClient.DefaultPort;
    private string _userName = string.Empty;
    private string _password = string.Empty;
    private Guid? _selectedClusterId;
    private string _statusText = string.Empty;
    private string _errorMessage = string.Empty;
    private int _readyToImportCount;
    private int _checkedCount;
    private int _duplicateCount;
    private IReadOnlyList<Infobase> _selectedBases = Array.Empty<Infobase>();
    private bool _importCompleted;
    private IReadOnlyList<ManagementServer> _availableServers = Array.Empty<ManagementServer>();
    private ManagementServer? _selectedServer;
    /// <summary>Кластер, переданный окном «Серверы 1С» для автовыбора (issue #25); null — не задан.</summary>
    private Guid? _requestedClusterId;

    private ICommand? _connectCommand;
    private ICommand? _loadBasesCommand;
    private ICommand? _importCommand;
    private ICommand? _selectAllCommand;
    private ICommand? _selectNoneCommand;

    /// <param name="rac">Клиент rac (кластеры, «cluster info», базы кластера).</param>
    /// <param name="existingInfobases">Базы, уже присутствующие в списке приложения — для пометки дубликатов.</param>
    /// <param name="dispatchToUi">
    /// Доставка применения результатов в UI-поток (передаёт окно); null — результаты
    /// применяются прямо из рабочего потока (тесты).
    /// </param>
    /// <param name="availableServers">
    /// Справочник «Сервера 1С» (issue #27): список выбора вместо четырёх полей
    /// адрес/порт/логин/пароль. Пароль берётся из выбранной записи — только в памяти
    /// окна, на диск не пишется. Опционально — старые вызовы без серверов работают
    /// как раньше (дефолты localhost:1540).
    /// </param>
    public ClusterImportViewModel(
        IRacClient rac,
        IReadOnlyList<Infobase> existingInfobases,
        Action<Action>? dispatchToUi = null,
        IReadOnlyList<ManagementServer>? availableServers = null)
    {
        _rac = rac ?? throw new ArgumentNullException(nameof(rac));
        _existingInfobases = existingInfobases ?? throw new ArgumentNullException(nameof(existingInfobases));
        _dispatchToUi = dispatchToUi;
        AvailableServers = availableServers ?? Array.Empty<ManagementServer>();
        try
        {
            _logger = AppServices.GetRequiredService<IAppLogger>();
        }
        catch
        {
            _logger = null; // тестовый контекст без DI
        }
    }

    // ---- Справочник «Сервера 1С» (issue #27) ----

    /// <summary>
    /// Записи справочника «Сервера 1С» для выбора подключения (вместо четырёх полей
    /// адрес/порт/логин/пароль). Заполняется окном; пустой список — в окне подсказка
    /// и кнопка «Подключиться» недоступна.
    /// </summary>
    public IReadOnlyList<ManagementServer> AvailableServers
    {
        get => _availableServers;
        set
        {
            if (SetProperty(ref _availableServers, value ?? Array.Empty<ManagementServer>()))
                OnPropertyChanged(nameof(HasAvailableServers));
        }
    }

    /// <summary>Есть ли хотя бы один сервер в справочнике (для доступности «Подключиться»).</summary>
    public bool HasAvailableServers => AvailableServers.Count > 0;

    /// <summary>
    /// Выбранный сервер справочника. При установке значения адрес/порт/логин/пароль
    /// заполняются из записи (пароль — только в памяти VM, на диск не пишется).
    /// Смена сервера сбрасывает состояние предыдущего подключения (issue #25):
    /// кластеры, чеклист и результат импорта очищаются, кнопка «Подключиться»
    /// становится снова доступной (окно пересчитывает её по событию SelectedServer).
    /// </summary>
    public ManagementServer? SelectedServer
    {
        get => _selectedServer;
        set
        {
            if (ReferenceEquals(_selectedServer, value))
                return;
            _selectedServer = value;
            OnPropertyChanged(nameof(SelectedServer));
            if (value is not null)
                FillFromServer(value);
            // Состояние подключения прежнего сервера больше не актуально.
            ResetConnectionState();
        }
    }

    // ===================== Параметры подключения =====================

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

    // ===================== Кластеры =====================

    /// <summary>Кластеры сервера (модели из rac «cluster list»).</summary>
    public IReadOnlyList<RacCluster> Clusters { get; private set; } = Array.Empty<RacCluster>();

    /// <summary>
    /// Выбранный кластер. При установке значения после подключения запускается
    /// загрузка информационных баз кластера (и «cluster info» для хоста подключения).
    /// </summary>
    public Guid? SelectedClusterId
    {
        get => _selectedClusterId;
        set
        {
            if (!SetProperty(ref _selectedClusterId, value))
                return;
            // Смена кластера сбрасывает предыдущий чеклист и результат импорта.
            ClearRows();
            SelectedBases = Array.Empty<Infobase>();
            ImportCompleted = false;
            OnPropertyChanged(nameof(SelectedBases));
            OnPropertyChanged(nameof(ImportCompleted));
            if (value is Guid id)
                StartLoadBases(id);
        }
    }

    // ===================== Чеклист =====================

    /// <summary>Строки чеклиста найденных баз выбранного кластера.</summary>
    public ObservableCollection<ClusterImportRow> Rows { get; } = new();

    /// <summary>Число отмеченных новых баз, готовых к импорту (сводка перед подтверждением).</summary>
    public int ReadyToImportCount
    {
        get => _readyToImportCount;
        private set => SetProperty(ref _readyToImportCount, value);
    }

    /// <summary>
    /// Число ВСЕХ отмеченных строк чеклиста (без фильтра дубликатов/пропусков, issue #41).
    /// Используется для доступности кнопки «Мигрировать»: для миграции наличие базы
    /// в списке приложения или причины пропуска при импорте не помеха — база переносится
    /// (или копируется) в другой кластер как есть. Проверки «уже есть в приёмнике»
    /// выполняются в диалоге миграции после выбора кластера назначения (1.0.0.52).
    /// </summary>
    public int CheckedCount
    {
        get => _checkedCount;
        private set => SetProperty(ref _checkedCount, value);
    }

    /// <summary>Число дубликатов и пропущенных баз («уже есть в списке»).</summary>
    public int DuplicateCount
    {
        get => _duplicateCount;
        private set => SetProperty(ref _duplicateCount, value);
    }

    /// <summary>Результат импорта: отмеченные новые базы (без дубликатов).</summary>
    public IReadOnlyList<Infobase> SelectedBases
    {
        get => _selectedBases;
        private set => SetProperty(ref _selectedBases, value);
    }

    /// <summary>Флаг успешного выполнения «Импортировать» (окно на этапе 3 использует его для закрытия).</summary>
    public bool ImportCompleted
    {
        get => _importCompleted;
        private set => SetProperty(ref _importCompleted, value);
    }

    /// <summary>
    /// Создавать группу по имени кластера при импорте (базы получают <c>Group = имя кластера</c>).
    /// Группировка станет опцией окна на этапе 3 — здесь параметр по умолчанию включён.
    /// </summary>
    public bool UseClusterGrouping { get; set; } = true;

    // ===================== Команды =====================

    /// <summary>«Подключиться»: список кластеров, при успехе — автовыбор первого и загрузка его баз.</summary>
    public ICommand ConnectCommand =>
        _connectCommand ??= new RelayCommand(async () => await ConnectAsync());

    /// <summary>«Загрузить»: перечитать базы выбранного кластера (после подключения).</summary>
    public ICommand LoadBasesCommand =>
        _loadBasesCommand ??= new RelayCommand(LoadBases);

    /// <summary>«Импортировать»: собрать отмеченные новые базы в <see cref="SelectedBases"/> и установить <see cref="ImportCompleted"/>.</summary>
    public ICommand ImportCommand =>
        _importCommand ??= new RelayCommand(Import);

    /// <summary>«Выделить все»: отметить все импортируемые (не дубликаты) строки.</summary>
    public ICommand SelectAllCommand =>
        _selectAllCommand ??= new RelayCommand(SelectAll);

    /// <summary>«Снять все»: снять отметки со всех строк чеклиста.</summary>
    public ICommand SelectNoneCommand =>
        _selectNoneCommand ??= new RelayCommand(SelectNone);

    // ===================== Статус =====================

    /// <summary>Строка состояния («Подключение…», «Загрузка баз…», итоги).</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>Подробное сообщение последней ошибки (текст rac/stderr как есть).</summary>
    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    /// <summary>Выполняется ли сейчас запрос (флаг занятости исключает наложение, см. ProcessInspectorViewModel).</summary>
    public bool IsBusy => _busy == 1;

    // ===================== Действия =====================

    /// <summary>
    /// Подставляет сервер справочника и (при <paramref name="autoConnect"/>) сразу
    /// подключается (issue #27). Вызывается окном импорта при открытии из окна
    /// «Серверы 1С»: адрес/порт/логин/пароль заполняются из записи, подключение
    /// запускается в UI-потоке после показа окна. Если в <see cref="AvailableServers"/>
    /// есть запись с тем же Id — выбирается она (один и тот же объект в комбобоксе).
    /// </summary>
    /// <param name="server">Запись справочника; может отличаться по ссылке от списка <see cref="AvailableServers"/>.</param>
    /// <param name="autoConnect">Сразу выполнить «Подключиться» (без ручного нажатия).</param>
    public Task InitializeFromServer(ManagementServer server, bool autoConnect, Guid? clusterId = null)
    {
        ArgumentNullException.ThrowIfNull(server);

        var matched = AvailableServers.FirstOrDefault(
            s => string.Equals(s.Id, server.Id, StringComparison.OrdinalIgnoreCase)) ?? server;

        // Выбор сервера (сеттер) сбрасывает состояние предыдущего подключения,
        // включая _requestedClusterId (issue #25), поэтому запрошенный кластер
        // запоминаем ПОСЛЕ установки SelectedServer — для автовыбора при
        // автоподключении: переданный кластер → единственный → выбор пользователем.
        SelectedServer = matched;
        _requestedClusterId = clusterId;

        return autoConnect ? ConnectAsync() : Task.CompletedTask;
    }

    /// <summary>
    /// «Подключиться»: получить список кластеров (rac «cluster list»); при успехе — выбрать
    /// первый кластер автоматически (он же запускает загрузку баз). Пустой адрес отклоняется
    /// валидацией без обращения к rac. Ошибки не роняют окно — пишутся в ErrorMessage/StatusText.
    /// Первая упавшая попытка (непрогретый формат вывода rac / холодный старт, issue #25)
    /// повторяется один раз БЕЗ показа ошибки — повторный вызов использует уже известный
    /// формат, и видимая «ошибка при успехе» не появляется.
    /// </summary>
    public async Task ConnectAsync()
    {
        if (string.IsNullOrWhiteSpace(ServerAddress))
        {
            ErrorMessage = LocalizationManager.T("ClusterImport.Error.AddressRequired");
            StatusText = string.Empty;
            return;
        }

        if (!TryEnterBusy())
            return;

        IReadOnlyList<RacCluster> clusters;
        try
        {
            ErrorMessage = string.Empty;
            StatusText = LocalizationManager.T("ClusterImport.Progress.Connect");
            TraceClusterImport("ConnectAsync: фаза «Подключение» начата (cluster list, issue #25).");

            clusters = await GetClustersWithRetryAsync(default).ConfigureAwait(false);

            TraceClusterImport($"ConnectAsync: «cluster list» успешен, кластеров: {clusters.Count} (issue #25).");
        }
        catch (RacClientException ex)
        {
            ErrorMessage = ex.Message;
            StatusText = LocalizationManager.T("ClusterImport.Status.ConnectFailed");
            TraceClusterImport($"ConnectAsync: RacClientException: {ex.Message} (issue #25).");
            return;
        }
        catch (OperationCanceledException)
        {
            StatusText = LocalizationManager.T("ClusterImport.Status.Cancelled");
            TraceClusterImport("ConnectAsync: OperationCanceledException — подключение отменено (issue #25).");
            return;
        }
        catch (Exception ex)
        {
            // Страховка: неожиданное исключение не должно оставлять «крутящийся» прогресс
            // (issue #25) — флаг занятости снимается в finally, а причина попадает в журнал.
            ErrorMessage = ex.Message;
            StatusText = LocalizationManager.T("ClusterImport.Status.ConnectFailed");
            _logger?.Error("Ошибка подключения к кластеру 1С", ex);
            TraceClusterImport($"ConnectAsync: Exception ({ex.GetType().Name}): {ex.Message} (issue #25).");
            return;
        }
        finally
        {
            ExitBusy();
        }

        // Применение результата и автовыбор — ЕДИНЫМ блоком в UI-потоке (issue #25):
        // окна доставляют действия асинхронным диспетчером (WPF Dispatcher.BeginInvoke,
        // Avalonia Dispatcher.Post), поэтому автовыбор ПОСЛЕ «применения кластеров»
        // читал устаревший (ещё не применённый) пустой Clusters и не срабатывал —
        // кластер приходилось выбирать вручную, а «первая ошибка» оставалась в окне.
        // ExitBusy уже выполнен (finally): сеттер SelectedClusterId свободно запустит
        // загрузку баз (TryEnterBusy) сразу после автовыбора.
        if (_dispatchToUi is null)
        {
            CompleteConnect(clusters);
        }
        else
        {
            try
            {
                _dispatchToUi(() => CompleteConnect(clusters));
            }
            catch
            {
                // Диспетчер может быть недоступен (закрывающееся окно) — применяем напрямую.
                CompleteConnect(clusters);
            }
        }
    }

    /// <summary>
    /// Применяет результаты подключения в UI-потоке ЕДИНЫМ блоком (issue #25): наполнение
    /// списка кластеров, сброс прежнего выбора, статус подключения и автовыбор — заполнение
    /// <see cref="Clusters"/> и выбор <see cref="SelectedClusterId"/> не должны разделяться
    /// асинхронной доставкой диспетчера (иначе автовыбор видит пустой список и не срабатывает).
    /// Автовыбор: переданный из окна «Серверы 1С» кластер → единственный кластер → подсказка
    /// «выберите кластер» (при нескольких и без переданного). Вызывается синхронно
    /// (dispatch=null, тесты) либо из dispatch-действия (реальные окна).
    /// </summary>
    private void CompleteConnect(IReadOnlyList<RacCluster> clusters)
    {
        Clusters = clusters;
        OnPropertyChanged(nameof(Clusters));
        // Сброс выбора: подключение к другому серверу — чужие базы не показываем.
        SelectedClusterId = null;
        ClearRows();

        StatusText = clusters.Count == 0
            ? LocalizationManager.T("ClusterImport.Status.NoClusters")
            : string.Format(LocalizationManager.T("ClusterImport.Status.ConnectedFormat"), clusters.Count);

        if (SelectedClusterId is null)
        {
            if (_requestedClusterId is Guid requested
                && clusters.Any(c => c.Id == requested))
            {
                SelectedClusterId = requested;
                TraceClusterImport($"ConnectAsync: автовыбор — переданный кластер {requested} (issue #25).");
            }
            else if (clusters.Count == 1)
            {
                SelectedClusterId = clusters[0].Id;
                TraceClusterImport($"ConnectAsync: автовыбор — единственный кластер {clusters[0].Id} (issue #25).");
            }
            else if (clusters.Count > 1)
            {
                StatusText = LocalizationManager.T("ClusterImport.Status.SelectCluster");
                TraceClusterImport(
                    $"ConnectAsync: автовыбор — несколько кластеров ({clusters.Count}), ожидается выбор пользователя (issue #25).");
            }
        }
    }

    /// <summary>
    /// Запрос «cluster list» с ОДНИМ повтором первой упавшей попытки (issue #25): первый
    /// в сессии запуск rac / непрогретый формат вывода может упасть — до показа ошибки
    /// пользователю запрос выполняется повторно. Повторная неудача — исключение уходит
    /// в catch ConnectAsync (ErrorMessage/StatusText, «Подключение не удалось»).
    /// </summary>
    private async Task<IReadOnlyList<RacCluster>> GetClustersWithRetryAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return await _rac.GetClustersAsync(BuildParams(), cancellationToken).ConfigureAwait(false);
        }
        catch (RacClientException ex)
        {
            TraceClusterImport($"ConnectAsync: первая попытка «cluster list» упала " +
                               $"(RacClientException: {ex.Message}); выполняется повтор без показа ошибки (issue #25).");
            return await _rac.GetClustersAsync(BuildParams(), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Запрос «infobase summary list» с ОДНИМ повтором первой упавшей попытки (issue #25):
    /// первая команда для холодного rac может упасть (старт процесса/формат вывода 8.5.1) —
    /// повтор БЕЗ установки ErrorMessage; успех повтора оставляет окно чистым.
    /// </summary>
    private async Task<IReadOnlyList<RacInfobaseSummary>> GetInfobasesWithRetryAsync(
        RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken)
    {
        try
        {
            return await _rac.GetInfobasesAsync(parameters, clusterId, cancellationToken).ConfigureAwait(false);
        }
        catch (RacClientException ex)
        {
            TraceClusterImport($"LoadBasesAsync({clusterId}): первая попытка «infobase summary list» упала " +
                               $"(RacClientException: {ex.Message}); выполняется повтор без показа ошибки (issue #25).");
            return await _rac.GetInfobasesAsync(parameters, clusterId, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Диагностика импорта из кластера (CM_CLUSTERIMPORT, issue #25): фазы подключения и
    /// загрузки баз, автовыбор, повторные попытки и ошибки с типом исключения. Флаг
    /// включается в trace.json («CM_CLUSTERIMPORT": true) либо env CM_CLUSTERIMPORT=1; лог —
    /// через обычный журнал приложения, ТОЛЬКО без пароля и секретов.
    /// </summary>
    private void TraceClusterImport(string message)
    {
        if (_logger is null || !TraceFlags.IsEnabled(TraceFlags.ClusterImportFlag))
            return;
        _logger.Info($"[CM_CLUSTERIMPORT] {message}");
    }

    /// <summary>«Загрузить»: перечитать базы выбранного кластера (без подключения — no-op).</summary>
    public void LoadBases()
    {
        if (SelectedClusterId is Guid id)
            StartLoadBases(id);
    }

    /// <summary>
    /// Загружает информационные базы кластера (rac «infobase summary list --cluster=...»)
    /// и «cluster info» для хоста строки подключения (кэшируется по clusterId). После await —
    /// сверка актуальности <see cref="SelectedClusterId"/>: устаревший результат быстрой смены
    /// кластера не применяется, вместо этого запускается загрузка актуального кластера.
    /// Первая упавшая попытка «infobase summary list» повторяется БЕЗ показа ошибки (issue #25);
    /// успешное завершение гарантированно очищает <see cref="ErrorMessage"/>.
    /// </summary>
    public async Task LoadBasesAsync(Guid clusterId, CancellationToken cancellationToken = default)
    {
        if (!TryEnterBusy())
            return;

        var stale = false;
        try
        {
            ErrorMessage = string.Empty;
            StatusText = LocalizationManager.T("ClusterImport.Progress.LoadBases");
            TraceClusterImport($"LoadBasesAsync({clusterId}): фаза «Загрузка баз» начата (issue #25).");

            var cluster = Clusters.FirstOrDefault(c => c.Id == clusterId);
            if (cluster is null)
            {
                TraceClusterImport($"LoadBasesAsync({clusterId}): кластер не найден в Clusters — выход (issue #25).");
                return;
            }

            var parameters = BuildParams();

            if (!_clusterInfoCache.TryGetValue(clusterId, out var info))
            {
                // «cluster info» нужен только для подписи хоста в строке подключения —
                // его сбой не должен блокировать загрузку баз и оставлять ошибку в окне
                // (issue #25): исключение пишем в журнал и продолжаем с info = null.
                try
                {
                    info = await _rac.GetClusterInfoAsync(parameters, clusterId, cancellationToken).ConfigureAwait(false);
                    TraceClusterImport($"LoadBasesAsync({clusterId}): «cluster info» получен (issue #25).");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger?.Error($"RAC: cluster info для кластера {clusterId} завершился ошибкой " +
                                   "(пропущен, загрузка баз продолжается, issue #25)", ex);
                    TraceClusterImport($"LoadBasesAsync({clusterId}): «cluster info» пропущен " +
                                       $"({ex.GetType().Name}: {ex.Message}) — загрузка баз продолжается (issue #25).");
                    info = null;
                }
                _clusterInfoCache[clusterId] = info;
            }

            var summaries = await GetInfobasesWithRetryAsync(parameters, clusterId, cancellationToken).ConfigureAwait(false);
            TraceClusterImport($"LoadBasesAsync({clusterId}): «infobase summary list» успешен, баз: {summaries.Count} (issue #25).");

            // Кластер сменился, пока шла загрузка — результаты устаревшего запроса не применяем.
            if (SelectedClusterId != clusterId)
            {
                stale = true;
                return;
            }

            ApplyRows(summaries, cluster.Port, cluster.Name, info?.HostName);

            // Гарантия очистки (issue #25): успех любой ветки стирает остаточную ошибку.
            ErrorMessage = string.Empty;
            StatusText = summaries.Count == 0
                ? LocalizationManager.T("ClusterImport.Error.NoBases")
                : string.Format(LocalizationManager.T("ClusterImport.Status.LoadedFormat"), summaries.Count);
            TraceClusterImport($"LoadBasesAsync({clusterId}): загрузка завершена успешно, " +
                               $"ErrorMessage='{ErrorMessage}', StatusText='{StatusText}' (issue #25).");
        }
        catch (RacClientException ex)
        {
            ErrorMessage = ex.Message;
            StatusText = LocalizationManager.T("ClusterImport.Status.LoadFailed");
            TraceClusterImport($"LoadBasesAsync({clusterId}): RacClientException: {ex.Message} (issue #25).");
        }
        catch (OperationCanceledException)
        {
            StatusText = LocalizationManager.T("ClusterImport.Status.Cancelled");
            TraceClusterImport($"LoadBasesAsync({clusterId}): OperationCanceledException — отменено (issue #25).");
        }
        catch (Exception ex)
        {
            // Страховка (issue #25): неожиданное исключение не должно оставлять
            // «крутящийся» прогресс — флаг занятости снимается в finally.
            ErrorMessage = ex.Message;
            StatusText = LocalizationManager.T("ClusterImport.Status.LoadFailed");
            _logger?.Error("Ошибка загрузки баз кластера 1С", ex);
            TraceClusterImport($"LoadBasesAsync({clusterId}): Exception ({ex.GetType().Name}): {ex.Message} (issue #25).");
        }
        finally
        {
            ExitBusy();
        }

        // Если кластер сменился во время загрузки — запускаем загрузку актуального
        // (после ExitBusy, чтобы флаг занятости не заблокировал повторный вход).
        if (stale && SelectedClusterId is Guid current)
            StartLoadBases(current);
    }

    /// <summary>
    /// «Импортировать»: собрать отмеченные новые базы (не дубликаты) в
    /// <see cref="SelectedBases"/> и установить <see cref="ImportCompleted"/>. Повторный чек
    /// дубликата пользователем не приводит к импорту — фильтр на этом шаге.
    /// </summary>
    public void Import()
    {
        if (IsBusy)
            return;

        SelectedBases = Rows
            .Where(r => r.IsChecked && !r.IsDuplicate && string.IsNullOrEmpty(r.SkipReason) && r.Tag is not null)
            .Select(r => r.Tag)
            .ToList();
        ImportCompleted = true;
    }

    /// <summary>
    /// «Выделить все»: отметить все импортируемые строки (дубликаты и пропущенные
    /// базы не трогаем — импорт их и так пропускает, <see cref="ReadyToImportCount"/>
    /// считает только новые).
    /// </summary>
    public void SelectAll()
    {
        foreach (var row in Rows)
        {
            if (!row.IsDuplicate && string.IsNullOrEmpty(row.SkipReason))
                row.IsChecked = true;
        }
        UpdateSummary();
    }

    /// <summary>
    /// Подсказка над кнопкой «Импортировать»: почему она недоступна. Пустая строка —
    /// кнопка доступна или идёт запрос (виден индикатор занятости).
    /// </summary>
    public string ActionHint => BuildActionHint(IsBusy, Rows.Count, CheckedCount, ReadyToImportCount);

    /// <summary>Чистая функция подсказки над кнопкой «Импортировать» (для тестов и обеих платформ).</summary>
    public static string BuildActionHint(bool busy, int rowCount, int checkedCount, int readyToImportCount)
    {
        if (busy)
            return string.Empty;
        if (rowCount == 0)
            return LocalizationManager.T("ClusterImport.Hint.NoBases");
        if (checkedCount == 0)
            return LocalizationManager.T("ClusterImport.Hint.NothingChecked");
        if (readyToImportCount == 0)
            return LocalizationManager.T("ClusterImport.Hint.ImportOnlyDuplicates");
        return string.Empty;
    }

    /// <summary>«Снять все»: снять отметки со всех строк чеклиста.</summary>
    public void SelectNone()
    {
        foreach (var row in Rows)
            row.IsChecked = false;
        UpdateSummary();
    }

    // ===================== Внутреннее =====================

    /// <summary>
    /// Заполняет адрес/порт/логин/пароль из записи справочника (issue #27). Пароль
    /// берётся только в память VM — на диск не пишется (политика неизменна).
    /// </summary>
    private void FillFromServer(ManagementServer server)
    {
        ServerAddress = server.Address;
        ServerPort = server.Port;
        UserName = server.User;
        Password = server.Password;
    }

    // ===================== Тест-сеam (issue #25) =====================

    /// <summary>
    /// Тест-сеam: последняя задача загрузки баз, запущенная из fire-and-forget
    /// (<see cref="StartLoadBases"/>: сеттер <see cref="SelectedClusterId"/>, команда
    /// «Загрузить», докачка после устаревшего результата). Публичный API не меняется
    /// (internal): тесты ждут её завершения, чтобы детерминированно проверять чеклист
    /// (<see cref="Rows"/>) и статус «Загружено N» после автовыбора единственного
    /// кластера — без таймаут-циклов.
    /// </summary>
    internal Task? LastLoadBasesTask { get; private set; }

    /// <summary>
    /// Запускает загрузку баз кластера без ожидания (fire-and-forget) и запоминает
    /// задачу для тестов (<see cref="LastLoadBasesTask"/>). Ошибки обрабатывает сама
    /// <see cref="LoadBasesAsync"/> (статус/ошибка окна) — безопасно не ждать.
    /// </summary>
    private void StartLoadBases(Guid clusterId)
    {
        LastLoadBasesTask = LoadBasesAsync(clusterId);
    }

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

    private void ApplyRows(
        IReadOnlyList<RacInfobaseSummary> summaries,
        int clusterPort,
        string clusterName,
        string? clusterHostName)
    {
        void Apply()
        {
            ClearRows();
            foreach (var source in summaries)
            {
                var row = BuildRow(source, clusterPort, clusterName, clusterHostName);
                row.PropertyChanged += RowOnPropertyChanged;
                Rows.Add(row);
            }
            UpdateSummary();
        }

        if (_dispatchToUi is null)
            Apply();
        else
            _dispatchToUi(Apply);
    }

    /// <summary>
    /// Строит строку чеклиста: маппинг в базу списка приложения
    /// (<see cref="RacInfobaseMapper.ToInfobase"/>, группа — имя кластера при
    /// <see cref="UseClusterGrouping"/>) и пометка дубликатов по строке подключения
    /// (<see cref="RacInfobaseMapper.IsDuplicate"/> против существующих баз). Файловых баз
    /// в кластере не бывает (комментарий 7OH от 2026-10-18 20:24), поэтому пустой <c>Dbms</c>
    /// не считается причиной пропуска — база импортируется как клиент-серверная.
    /// </summary>
    private ClusterImportRow BuildRow(
        RacInfobaseSummary source,
        int clusterPort,
        string clusterName,
        string? clusterHostName)
    {
        var groupName = UseClusterGrouping ? clusterName : string.Empty;
        var infobase = RacInfobaseMapper.ToInfobase(source, ServerAddress, clusterPort, clusterHostName, groupName);

        string? skipReason = null;
        var isDuplicate = false;
        if (RacInfobaseMapper.IsDuplicate(_existingInfobases, infobase))
        {
            skipReason = LocalizationManager.T("ClusterImport.AlreadyExists");
            isDuplicate = true;
        }

        return new ClusterImportRow(
            source.Name,
            BuildSubtitle(source),
            clusterName,
            infobase.Connection?.ToConnectionString() ?? string.Empty,
            isChecked: skipReason is null,
            isDuplicate: isDuplicate,
            skipReason: skipReason,
            tag: infobase,
            source: source);
    }

    /// <summary>
    /// Подпись строки: описание базы и сведения о СУБД (тип • сервер • имя БД в СУБД),
    /// разделённые « • ». Пустые части опускаются.
    /// </summary>
    private static string BuildSubtitle(RacInfobaseSummary source)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(source.Descr))
            parts.Add(source.Descr.Trim());

        if (!string.IsNullOrWhiteSpace(source.Dbms))
        {
            var dbParts = new List<string> { source.Dbms.Trim() };
            if (!string.IsNullOrWhiteSpace(source.DbServer))
                dbParts.Add(source.DbServer.Trim());
            if (!string.IsNullOrWhiteSpace(source.DbName))
                dbParts.Add(source.DbName.Trim());
            parts.Add(string.Join(" • ", dbParts));
        }

        return string.Join(" • ", parts);
    }

    private void ClearRows()
    {
        foreach (var row in Rows)
            row.PropertyChanged -= RowOnPropertyChanged;
        Rows.Clear();
        UpdateSummary();
    }

    private void RowOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ClusterImportRow.IsChecked))
            UpdateSummary();
    }

    private void UpdateSummary()
    {
        ReadyToImportCount = Rows.Count(r => r.IsChecked && !r.IsDuplicate && string.IsNullOrEmpty(r.SkipReason));
        // Для «Мигрировать» нужен общий счётчик отмеченных (без фильтра дублей, issue #41).
        CheckedCount = Rows.Count(r => r.IsChecked);
        DuplicateCount = Rows.Count(r => r.IsDuplicate || !string.IsNullOrEmpty(r.SkipReason));
    }

    private bool TryEnterBusy()
    {
        // Один запрос за раз: длительная rac-команда не должна копить очередь (см. ProcessInspectorViewModel:80).
        if (Interlocked.Exchange(ref _busy, 1) == 1)
            return false;
        NotifyIsBusy();
        return true;
    }

    private void ExitBusy()
    {
        Interlocked.Exchange(ref _busy, 0);
        NotifyIsBusy();
    }

    /// <summary>
    /// Доставляет уведомление IsBusy в UI-поток через <see cref="_dispatchToUi"/>,
    /// чтобы окно обновляло кнопки и индикатор занятости на своём потоке (issue #25):
    /// WPF-кнопки нельзя переключать из фонового потока, иначе событие «теряется»
    /// и прогресс застревает. Без диспетчера (тесты) — синхронно, как раньше.
    /// </summary>
    private void NotifyIsBusy()
    {
        if (_dispatchToUi is null)
        {
            OnPropertyChanged(nameof(IsBusy));
            return;
        }
        _dispatchToUi(() => OnPropertyChanged(nameof(IsBusy)));
    }

    /// <summary>
    /// Сбрасывает состояние предыдущего подключения при смене сервера (issue #25):
    /// очищаются кластеры, чеклист, выбор кластера, результат импорта, ошибки/статус
    /// и запомненный автовыбор кластера (чтобы повторное «Подключиться» не тянуло
    /// кластер старого сервера).
    /// </summary>
    private void ResetConnectionState()
    {
        Clusters = Array.Empty<RacCluster>();
        OnPropertyChanged(nameof(Clusters));

        // Сеттер при null чистит строки чеклиста, выбор и ImportCompleted и не
        // запускает загрузку баз; после no-op сеттера строки чистим явно.
        SelectedClusterId = null;
        SelectedBases = Array.Empty<Infobase>();
        OnPropertyChanged(nameof(SelectedBases));
        ImportCompleted = false;
        OnPropertyChanged(nameof(ImportCompleted));
        ClearRows();

        ErrorMessage = string.Empty;
        StatusText = string.Empty;
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(StatusText));

        _requestedClusterId = null;
    }
}