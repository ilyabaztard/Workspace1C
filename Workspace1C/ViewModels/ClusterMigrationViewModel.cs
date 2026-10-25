using System;
using System.Collections.Generic;
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
/// ViewModel окна «Миграция между кластерами 1С» (issue #41, переработка по комментарию
/// 7OH): перенос информационных баз между кластерами серверов 1С. Окно самостоятельное
/// (не вызывается из импорта) и имеет ДВУХПАНЕЛЬНУЮ раскладку: группа «Источник»
/// (Сервер 1С + «Подключиться», Кластер + «Получить базы», «Загружено баз») и группа
/// «Приёмник» (те же поля; кнопка «Получить статусы» проставляет статусы приёмника
/// «Есть в кластере»/пусто по совпадению имени базы). Сервера источника и приёмника
/// не должны пересекаться — проверяется по выбору, но из списков не удаляются.
/// Галочки: после получения баз источника — ВСЕ; после получения статусов приёмника —
/// только там, где базы нет в приёмнике. Доступность «Мигрировать» зависит от галочек
/// и статусов: если у отмеченной строки статус приёмника «Есть в кластере» — кнопка
/// недоступна. По «Мигрировать» для КАЖДОЙ отмеченной базы: сначала <c>infobase create</c>
/// в новом кластере (те же данные, что в старом), затем — если галочка «Не удалять
/// в старом» не отмечена — <c>infobase drop</c> без <c>--drop-database</c>. Базы
/// добавляются в новую группу (имя кластера — приёмник), как в импорте. Чистый .NET
/// без платформенных зависимостей — обе платформы.
/// </summary>
public sealed class ClusterMigrationViewModel : ViewModelBase
{
    private readonly IRacClient _rac;
    private readonly IReadOnlyList<Infobase> _existingInfobases;
    private readonly Action<Action>? _dispatchToUi;
    private readonly IAppLogger? _logger;

    // ===================== Источник =====================
    private IReadOnlyList<ManagementServer> _availableSourceServers = Array.Empty<ManagementServer>();
    private ManagementServer? _selectedSourceServer;
    private IReadOnlyList<RacCluster> _sourceClusters = Array.Empty<RacCluster>();
    private Guid? _selectedSourceClusterId;
    // Кластер, запрошенный окном «Серверы 1С» для автовыбора (issue #41, паттерн
    // _requestedClusterId импорта, issue #25); потребляется ApplySourceClusters.
    private Guid? _requestedSourceClusterId;
    private string _sourceStatusText = string.Empty;
    private string _sourceError = string.Empty;

    // ===================== Приёмник =====================
    private IReadOnlyList<ManagementServer> _availableTargetServers = Array.Empty<ManagementServer>();
    private ManagementServer? _selectedTargetServer;
    private IReadOnlyList<RacCluster> _targetClusters = Array.Empty<RacCluster>();
    private Guid? _selectedTargetClusterId;
    private string _targetStatusText = string.Empty;
    private string _targetError = string.Empty;

    // ===================== Общее =====================
    private int _busy;
    private string _dbUser = string.Empty;
    private string _dbPassword = string.Empty;
    // «Тип СУБД»/«Сервер СУБД»/«Локаль» для rac «infobase create» (issue #41): автозаполняются
    // из первой базы источника, пока пользователь не изменил их вручную (_dbDetailsTouched).
    private string _dbms = RacDbms.MssqlServer;
    private string _dbServer = string.Empty;
    private string _locale = RacLocales.Default;
    private bool _dbDetailsTouched;
    private bool _keepInSource;
    private bool _useClusterGrouping = true;
    private string _statusText = string.Empty;
    private string _errorMessage = string.Empty;
    private int _progressIndex;
    private int _progressTotal;
    private string _currentBaseName = string.Empty;
    private bool _migrationCompleted;
    private ClusterMigrationResult? _result;

    private ICommand? _connectSourceCommand;
    private ICommand? _loadSourceBasesCommand;
    private ICommand? _connectTargetCommand;
    private ICommand? _getTargetStatusesCommand;
    private ICommand? _migrateCommand;
    private ICommand? _selectAllCommand;
    private ICommand? _selectNoneCommand;

    /// <param name="rac">Клиент rac (кластеры, «infobase summary list/create/drop»).</param>
    /// <param name="existingInfobases">Базы, уже присутствующие в списке приложения — для статуса источника («уже есть в списке»).</param>
    /// <param name="availableServers">Справочник «Серверы 1С» — полный список для обеих панелей (сервера не удаляются).</param>
    /// <param name="dispatchToUi">Доставка применения результатов в UI-поток; null — сразу (тесты).</param>
    public ClusterMigrationViewModel(
        IRacClient rac,
        IReadOnlyList<Infobase> existingInfobases,
        IReadOnlyList<ManagementServer> availableServers,
        Action<Action>? dispatchToUi = null)
    {
        _rac = rac ?? throw new ArgumentNullException(nameof(rac));
        _existingInfobases = existingInfobases ?? throw new ArgumentNullException(nameof(existingInfobases));
        _dispatchToUi = dispatchToUi;
        AvailableSourceServers = availableServers;
        AvailableTargetServers = availableServers;
        try
        {
            _logger = AppServices.GetRequiredService<IAppLogger>();
        }
        catch
        {
            _logger = null; // тестовый контекст без DI
        }
    }

    /// <summary>
    /// Подставляет сервер-источник при открытии окна из «Серверы 1С» (issue #41, комментарий
    /// 7OH: «текущий сервер, как в Импорте, не подставляется, хотя его кластер подставлен»).
    /// <see cref="ManagementServer"/> — class без переопределённого Equals, поэтому переданный
    /// экземпляр по ссылке не равен записям <see cref="AvailableSourceServers"/> (список из
    /// справочника): сопоставляем по Id (как <see cref="ClusterImportViewModel.InitializeFromServer"/>)
    /// — выбирается запись списка, иначе — переданный экземпляр. Сеттер
    /// <see cref="SelectedSourceServer"/> сразу запускает <c>StartConnectSource()</c>.
    /// Запрошенный кластер запоминается для автовыбора по завершении загрузки кластеров
    /// (паттерн <c>_requestedClusterId</c> импорта, issue #25) и выставляется сразу — гонки
    /// с автозагрузкой не создаётся (безвредный сеттер на пустом списке кластеров выходит
    /// рано, а <c>ApplySourceClusters</c> повторит автовыбор по запомненному Id).
    /// </summary>
    /// <param name="server">Сервер из окна «Серверы 1С» (может отличаться по ссылке от списка); null — обычное открытие.</param>
    /// <param name="clusterId">Кластер, выбранный в окне «Серверы 1С»; null — без автовыбора.</param>
    public void SetInitialSource(ManagementServer? server, Guid? clusterId)
    {
        if (server is not null)
        {
            var matched = AvailableSourceServers.FirstOrDefault(
                s => string.Equals(s.Id, server.Id, StringComparison.OrdinalIgnoreCase)) ?? server;
            SelectedSourceServer = matched;
        }

        _requestedSourceClusterId = clusterId;
        if (clusterId is Guid id)
            SelectedSourceClusterId = id;
    }

    // ===================== Справочник «Серверы 1С» =====================

    /// <summary>Записи справочника для панели «Источник» (полный список — сервера не удаляются).</summary>
    public IReadOnlyList<ManagementServer> AvailableSourceServers
    {
        get => _availableSourceServers;
        private set
        {
            if (SetProperty(ref _availableSourceServers, value ?? Array.Empty<ManagementServer>()))
                OnPropertyChanged(nameof(HasAvailableServers));
        }
    }

    /// <summary>Записи справочника для панели «Приёмник» (полный список — сервера не удаляются).</summary>
    public IReadOnlyList<ManagementServer> AvailableTargetServers
    {
        get => _availableTargetServers;
        private set
        {
            if (SetProperty(ref _availableTargetServers, value ?? Array.Empty<ManagementServer>()))
                OnPropertyChanged(nameof(HasAvailableServers));
        }
    }

    /// <summary>Есть ли серверы в справочнике (иначе «Подключиться» недоступно).</summary>
    public bool HasAvailableServers => AvailableSourceServers.Count > 0;

    // ===================== Панель «Источник» =====================

    /// <summary>
    /// Выбранный сервер-источник. При установке значения запускается загрузка его
    /// кластеров («cluster list»); состояние прежнего подключения сбрасывается.
    /// </summary>
    public ManagementServer? SelectedSourceServer
    {
        get => _selectedSourceServer;
        set
        {
            if (ReferenceEquals(_selectedSourceServer, value))
                return;
            _selectedSourceServer = value;
            OnPropertyChanged(nameof(SelectedSourceServer));
            OnPropertyChanged(nameof(ServersOverlap));
            OnPropertyChanged(nameof(CanMigrate));
            OnPropertyChanged(nameof(MigrateHint));
            if (value is not null)
                StartConnectSource();
        }
    }

    /// <summary>Кластеры выбранного сервера-источника (модели rac «cluster list»).</summary>
    public IReadOnlyList<RacCluster> SourceClusters
    {
        get => _sourceClusters;
        private set
        {
            if (SetProperty(ref _sourceClusters, value ?? Array.Empty<RacCluster>()))
                OnPropertyChanged(nameof(SourceClusters));
        }
    }

    /// <summary>
    /// Выбранный кластер-источник. Единственный кластер сервера выбирается автоматически
    /// после загрузки. При выборе кластера запускается загрузка его баз («Получить базы»).
    /// </summary>
    public Guid? SelectedSourceClusterId
    {
        get => _selectedSourceClusterId;
        set
        {
            if (SetProperty(ref _selectedSourceClusterId, value))
            {
                OnPropertyChanged(nameof(CanMigrate));
                OnPropertyChanged(nameof(MigrateHint));
                if (value is Guid id)
                    StartLoadSourceBases(id);
            }
        }
    }

    /// <summary>Строки чеклиста баз кластера-источника.</summary>
    public ObservableCollection<ClusterMigrationRow> SourceRows { get; } = new();

    /// <summary>Статус панели «Источник» («Загружено баз: N», ошибки подключения).</summary>
    public string SourceStatusText
    {
        get => _sourceStatusText;
        private set => SetProperty(ref _sourceStatusText, value);
    }

    /// <summary>Ошибка панели «Источник» (текст rac/stderr как есть).</summary>
    public string SourceError
    {
        get => _sourceError;
        private set => SetProperty(ref _sourceError, value);
    }

    // ===================== Панель «Приёмник» =====================

    /// <summary>
    /// Выбранный сервер-приёмник. При установке значения запускается загрузка его
    /// кластеров («cluster list»); состояние прежнего подключения сбрасывается.
    /// </summary>
    public ManagementServer? SelectedTargetServer
    {
        get => _selectedTargetServer;
        set
        {
            if (ReferenceEquals(_selectedTargetServer, value))
                return;
            _selectedTargetServer = value;
            OnPropertyChanged(nameof(SelectedTargetServer));
            OnPropertyChanged(nameof(ServersOverlap));
            OnPropertyChanged(nameof(CanMigrate));
            OnPropertyChanged(nameof(MigrateHint));
            if (value is not null)
                StartConnectTarget();
        }
    }

    /// <summary>Кластеры выбранного сервера-приёмника (модели rac «cluster list»).</summary>
    public IReadOnlyList<RacCluster> TargetClusters
    {
        get => _targetClusters;
        private set
        {
            if (SetProperty(ref _targetClusters, value ?? Array.Empty<RacCluster>()))
                OnPropertyChanged(nameof(TargetClusters));
        }
    }

    /// <summary>
    /// Выбранный кластер-приёмник. Единственный кластер сервера выбирается автоматически
    /// после загрузки. При выборе кластера запускается «Получить статусы» (если базы
    /// источника уже загружены).
    /// </summary>
    public Guid? SelectedTargetClusterId
    {
        get => _selectedTargetClusterId;
        set
        {
            if (SetProperty(ref _selectedTargetClusterId, value))
            {
                OnPropertyChanged(nameof(CanMigrate));
                OnPropertyChanged(nameof(MigrateHint));
                if (value is Guid id)
                    StartGetTargetStatuses(id);
            }
        }
    }

    /// <summary>Статус панели «Приёмник» («Кластеров: N», ошибки подключения).</summary>
    public string TargetStatusText
    {
        get => _targetStatusText;
        private set => SetProperty(ref _targetStatusText, value);
    }

    /// <summary>Ошибка панели «Приёмник» (текст rac/stderr как есть).</summary>
    public string TargetError
    {
        get => _targetError;
        private set => SetProperty(ref _targetError, value);
    }

    /// <summary>
    /// Сервера источника и приёмника не должны пересекаться (issue #41): true — выбран
    /// один и тот же сервер (по Id) в обеих панелях. Из списков сервера НЕ удаляются —
    /// пользователь может вернуть прежний выбор; «Мигрировать» при пересечении недоступна.
    /// </summary>
    public bool ServersOverlap =>
        SelectedSourceServer is { } source
        && SelectedTargetServer is { } target
        && string.Equals(source.Id?.Trim(), target.Id?.Trim(), StringComparison.OrdinalIgnoreCase);

    // ===================== Учётные данные СУБД и опции =====================

    /// <summary>Пользователь СУБД для регистрации баз в новом кластере (rac «--db-user»).</summary>
    public string DbUser
    {
        get => _dbUser;
        set => SetProperty(ref _dbUser, value ?? string.Empty);
    }

    /// <summary>
    /// Пароль СУБД (rac «--db-pwd»). rac НЕ возвращает пароль СУБД — его вводит
    /// пользователь для подключения нового кластера к уже существующей БД.
    /// Живёт только в памяти окна; в журнале маскируется RacClient.
    /// </summary>
    public string DbPassword
    {
        get => _dbPassword;
        set => SetProperty(ref _dbPassword, value ?? string.Empty);
    }

    /// <summary>
    /// Варианты «Тип СУБД» для выпадающего списка миграции (rac «--dbms»): ровно четыре
    /// клиент-серверных значения (комментарий 7OH от 2026-10-18 20:24), как в диалоге
    /// создания базы.
    /// </summary>
    public IReadOnlyList<string> DbmsOptions { get; } = new[]
    {
        RacDbms.MssqlServer,
        RacDbms.PostgreSql,
        RacDbms.IbmDb2,
        RacDbms.OracleDatabase
    };

    /// <summary>
    /// Тип СУБД для регистрации баз в кластере-приёмнике (rac «--dbms», issue #41, комментарий
    /// 7OH): «странная ошибка» создания базы возникала, потому что rac требует тип СУБД и
    /// сервер СУБД, а для приёмника на другом сервере СУБД значения из записи источника не
    /// подходят. Поле на форме (ComboBox из <see cref="DbmsOptions"/>), выше учётных данных
    /// СУБД. Автозаполняется из первой базы источника после «Получить базы»; ручное изменение
    /// пользователем фиксируется флагом <c>_dbDetailsTouched</c>, и автозаполнение больше не
    /// перезаписывает значение.
    /// </summary>
    public string Dbms
    {
        get => _dbms;
        set
        {
            _dbDetailsTouched = true;
            SetProperty(ref _dbms, (value ?? string.Empty).Trim());
        }
    }

    /// <summary>
    /// Сервер СУБД для регистрации баз в кластере-приёмнике (rac «--db-server», issue #41):
    /// rac «infobase create» требует непустые Dbms и DbServer. Поле на форме (TextBox выше
    /// учётных данных СУБД); автозаполняется из первой базы источника, пока пользователь
    /// не изменил его вручную.
    /// </summary>
    public string DbServer
    {
        get => _dbServer;
        set
        {
            _dbDetailsTouched = true;
            SetProperty(ref _dbServer, (value ?? string.Empty).Trim());
        }
    }

    /// <summary>
    /// Варианты локали для выпадающего списка миграции (issue #41, комментарий 7OH:
    /// «Поле с локалью на поле не замечено»): те же значения, что при добавлении базы
    /// — <see cref="RacLocales.Values"/>.
    /// </summary>
    public IReadOnlyList<string> LocaleOptions { get; } = Models.RacLocales.Values;

    /// <summary>
    /// Локаль для регистрации баз в кластере-приёмнике (rac «--locale», issue #41): берётся
    /// из источника автоматически (некоторые версии rac 8.3/8.5 не отдают locale в
    /// «infobase summary list» — пустое значение ломало создание базы). Поле на форме
    /// (ComboBox из <see cref="LocaleOptions"/>) после логина/пароля; автозаполняется из
    /// первой базы источника, пока пользователь не изменил вручную.
    /// </summary>
    public string Locale
    {
        get => _locale;
        set
        {
            _dbDetailsTouched = true;
            SetProperty(ref _locale, Models.RacLocales.Normalize(value ?? string.Empty));
        }
    }

    /// <summary>
    /// «Не удалять в старом кластере (копия без удаления)» (issue #41): true — база
    /// копируется в новый кластер БЕЗ удаления из старого; false — после успешного
    /// добавления база удаляется из старого кластера командой rac «infobase drop»
    /// БЕЗ --drop-database (файл/БД на сервере СУБД остаётся).
    /// </summary>
    public bool KeepInSource
    {
        get => _keepInSource;
        set => SetProperty(ref _keepInSource, value);
    }

    /// <summary>
    /// «Создавать группу по имени кластера» (как в импорте): true — перенесённые базы
    /// добавляются в новую группу с именем кластера-приёмника; false — без группы.
    /// </summary>
    public bool UseClusterGrouping
    {
        get => _useClusterGrouping;
        set => SetProperty(ref _useClusterGrouping, value);
    }

    // ===================== Сводка, галочки и доступность =====================

    /// <summary>Число отмеченных строк (для переноса).</summary>
    public int CheckedCount => SourceRows.Count(r => r.IsChecked);

    /// <summary>
    /// Сводка чеклиста «сколько выделено и сколько будет перенесено» (issue #41,
    /// комментарий 7OH, п. «сводка выделения»): «Выделено: {0} из {1} · будет
    /// перенесено: {2}». «Будет перенесено» — отмеченные базы без статуса приёмника
    /// «Есть в кластере» (такие Мигрировать не переносит). Пустой чеклист — пустая строка.
    /// </summary>
    public string SelectionSummaryText
    {
        get
        {
            if (SourceRows.Count == 0)
                return string.Empty;
            var toMigrate = SourceRows.Count(r => r.IsChecked && string.IsNullOrEmpty(r.TargetStatus));
            var summary = string.Format(
                LocalizationManager.T("ClusterMigration.Summary.SelectedFormat"),
                CheckedCount, SourceRows.Count, toMigrate);
            if (CheckedCount > toMigrate)
                summary += " " + LocalizationManager.T("ClusterMigration.Summary.InTargetPending");
            return summary;
        }
    }

    /// <summary>
    /// Есть ли отмеченная строка со статусом приёмника «Есть в кластере» (issue #41):
    /// если да — «Мигрировать» недоступна (перенос такой базы rac не выполнит).
    /// </summary>
    public bool HasCheckedInTarget =>
        SourceRows.Any(r => r.IsChecked && !string.IsNullOrEmpty(r.TargetStatus));

    /// <summary>
    /// Доступность «Мигрировать» (issue #41): меняется после изменения галочек и
    /// статусов. Кнопка недоступна, если сервера пересекаются, не выбраны сервер/кластер
    /// источника или приёмника, не отмечено ни одной базы или у отмеченной строки
    /// статус приёмника «Есть в кластере».
    /// </summary>
    public bool CanMigrate =>
        !IsBusy
        && !ServersOverlap
        && SelectedSourceServer is not null
        && SelectedSourceClusterId is not null
        && SelectedTargetServer is not null
        && SelectedTargetClusterId is not null
        && CheckedCount > 0
        && !HasCheckedInTarget;

    /// <summary>
    /// Причина недоступности «Мигрировать» (над кнопкой). Пустая строка — кнопка
    /// доступна или идёт запрос (виден индикатор занятости).
    /// </summary>
    public string MigrateHint
    {
        get
        {
            if (IsBusy)
                return string.Empty;
            if (ServersOverlap)
                return LocalizationManager.T("ClusterMigration.Hint.SameServer");
            if (SelectedSourceServer is null || SelectedSourceClusterId is null)
                return LocalizationManager.T("ClusterMigration.Hint.NoSource");
            if (SelectedTargetServer is null || SelectedTargetClusterId is null)
                return LocalizationManager.T("ClusterMigration.Hint.NoTarget");
            if (CheckedCount == 0)
                return LocalizationManager.T("ClusterMigration.Hint.NothingChecked");
            if (HasCheckedInTarget)
                return LocalizationManager.T("ClusterMigration.Hint.InTarget");
            return string.Empty;
        }
    }

    // ===================== Прогресс и результат =====================

    /// <summary>Текущая обрабатываемая база («какая база обрабатывается», issue #41).</summary>
    public string CurrentBaseName
    {
        get => _currentBaseName;
        private set => SetProperty(ref _currentBaseName, value);
    }

    /// <summary>Номер обрабатываемой базы (счётчик «1 из 10», 1-based).</summary>
    public int ProgressIndex
    {
        get => _progressIndex;
        private set
        {
            if (SetProperty(ref _progressIndex, value))
                OnPropertyChanged(nameof(ProgressText));
        }
    }

    /// <summary>Общее число баз к переносу.</summary>
    public int ProgressTotal
    {
        get => _progressTotal;
        private set
        {
            if (SetProperty(ref _progressTotal, value))
                OnPropertyChanged(nameof(ProgressText));
        }
    }

    /// <summary>Строка прогресса «N из M: база» (issue #41, фоном в диалоге).</summary>
    public string ProgressText =>
        ProgressTotal <= 0
            ? string.Empty
            : string.Format(
                LocalizationManager.T("ClusterMigration.Progress.Format"), ProgressIndex, ProgressTotal, CurrentBaseName);

    /// <summary>Флаг успешного завершения переноса (окно спрашивает «заменить строку подключения?»).</summary>
    public bool MigrationCompleted
    {
        get => _migrationCompleted;
        private set => SetProperty(ref _migrationCompleted, value);
    }

    /// <summary>Результат миграции (валиден при <see cref="MigrationCompleted"/>).</summary>
    public ClusterMigrationResult? Result
    {
        get => _result;
        private set => SetProperty(ref _result, value);
    }

    /// <summary>
    /// Выставляет ответ пользователя на вопрос «Хотите заменить строку подключения?»
    /// (окно показывает его после <see cref="MigrationCompleted"/>): true — MainViewModel
    /// заменит сервер/порт в базах списка приложения.
    /// </summary>
    public void SetReplaceConnectionStrings(bool value)
    {
        if (Result is not null)
            Result = Result with { ReplaceConnectionStrings = value };
    }

    // ===================== Команды =====================

    /// <summary>«Подключиться» (источник): загрузить кластеры выбранного сервера-источника.</summary>
    public ICommand ConnectSourceCommand =>
        _connectSourceCommand ??= new RelayCommand(() => _ = ConnectSourceAsync());

    /// <summary>«Получить базы» (источник): загрузить базы выбранного кластера-источника.</summary>
    public ICommand LoadSourceBasesCommand =>
        _loadSourceBasesCommand ??= new RelayCommand(LoadSourceBases);

    /// <summary>«Подключиться» (приёмник): загрузить кластеры выбранного сервера-приёмника.</summary>
    public ICommand ConnectTargetCommand =>
        _connectTargetCommand ??= new RelayCommand(() => _ = ConnectTargetAsync());

    /// <summary>«Получить статусы» (приёмник): проставить статусы приёмника по совпадению имён.</summary>
    public ICommand GetTargetStatusesCommand =>
        _getTargetStatusesCommand ??= new RelayCommand(() => _ = GetTargetStatusesAsync());

    /// <summary>«Мигрировать»: добавить базы в новый кластер и удалить из старого (по настройке).</summary>
    public ICommand MigrateCommand =>
        _migrateCommand ??= new RelayCommand(() => _ = MigrateAsync());

    /// <summary>«Выделить все»: отметить все строки чеклиста.</summary>
    public ICommand SelectAllCommand =>
        _selectAllCommand ??= new RelayCommand(SelectAll);

    /// <summary>«Снять все»: снять отметки со всех строк чеклиста.</summary>
    public ICommand SelectNoneCommand =>
        _selectNoneCommand ??= new RelayCommand(SelectNone);

    // ===================== Статус =====================

    /// <summary>Строка состояния («Загрузка кластеров…», прогресс, итог).</summary>
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

    /// <summary>Выполняется ли сейчас запрос (флаг занятости исключает наложение).</summary>
    public bool IsBusy => _busy == 1;

    // ===================== Действия: источник =====================

    /// <summary>
    /// Загружает кластеры выбранного сервера-источника («cluster list», фазовый паттерн
    /// окна импорта, issue #25): результат применяется в UI-потоке единым блоком,
    /// автовыбор единственного кластера выполняется там же. Ошибки не роняют окно —
    /// пишутся в <see cref="SourceError"/>/<see cref="SourceStatusText"/>.
    /// </summary>
    public async Task ConnectSourceAsync()
    {
        if (!TryEnterBusy())
            return;

        IReadOnlyList<RacCluster> clusters;
        try
        {
            SourceError = string.Empty;
            SourceStatusText = LocalizationManager.T("ClusterMigration.Status.LoadingClusters");
            if (SelectedSourceServer is not { } server)
                return;

            clusters = await _rac.GetClustersAsync(BuildParams(server)).ConfigureAwait(false);
        }
        catch (RacClientException ex)
        {
            SourceError = ex.Message;
            SourceStatusText = LocalizationManager.T("ClusterMigration.Status.ConnectFailed");
            return;
        }
        catch (OperationCanceledException)
        {
            SourceStatusText = LocalizationManager.T("ClusterMigration.Status.Cancelled");
            return;
        }
        catch (Exception ex)
        {
            SourceError = ex.Message;
            SourceStatusText = LocalizationManager.T("ClusterMigration.Status.ConnectFailed");
            _logger?.Error("Ошибка загрузки кластеров сервера-источника (миграция баз)", ex);
            return;
        }
        finally
        {
            ExitBusy();
        }

        Dispatch(() => ApplySourceClusters(clusters));
    }

    /// <summary>Применяет список кластеров источника в UI-потоке единым блоком (автовыбор единственного).</summary>
    private void ApplySourceClusters(IReadOnlyList<RacCluster> clusters)
    {
        SourceClusters = clusters;
        SelectedSourceClusterId = null;
        SourceStatusText = clusters.Count == 0
            ? LocalizationManager.T("ClusterMigration.Status.NoClusters")
            : string.Format(LocalizationManager.T("ClusterMigration.Status.LoadedClustersFormat"), clusters.Count);
        // Автовыбор кластера (issue #41): приоритет у запрошенного из окна «Серверы 1С»
        // (паттерн _requestedClusterId импорта, issue #25) — если присвоить id сразу в
        // SetInitialSource, этот блок перезапишет его сбросом выше, поэтому id запоминается
        // и выбирается здесь; иначе — автовыбор единственного кластера, как раньше.
        if (_requestedSourceClusterId is Guid requested && clusters.Any(c => c.Id == requested))
        {
            SelectedSourceClusterId = requested;
            _requestedSourceClusterId = null;
        }
        else if (clusters.Count == 1)
        {
            SelectedSourceClusterId = clusters[0].Id;
        }
    }

    /// <summary>«Получить базы»: перечитать базы выбранного кластера-источника (без подключения — no-op).</summary>
    public void LoadSourceBases()
    {
        if (SelectedSourceClusterId is Guid id)
            StartLoadSourceBases(id);
    }

    /// <summary>
    /// Загружает информационные базы кластера-источника (rac «infobase summary list»),
    /// строит чеклист <see cref="SourceRows"/> и ставит ВСЕ галочки (issue #41).
    /// Статус источника («уже есть в списке») вычисляется по <see cref="_existingInfobases"/>.
    /// </summary>
    public async Task LoadSourceBasesAsync(Guid clusterId)
    {
        if (!TryEnterBusy())
            return;

        try
        {
            SourceError = string.Empty;
            SourceStatusText = LocalizationManager.T("ClusterMigration.Status.LoadingBases");
            if (SelectedSourceServer is not { } server)
                return;

            var cluster = SourceClusters.FirstOrDefault(c => c.Id == clusterId);
            if (cluster is null)
                return;

            var summaries = await _rac.GetInfobasesAsync(BuildParams(server), clusterId).ConfigureAwait(false);
            if (SelectedSourceClusterId != clusterId)
                return; // кластер сменился — результат устарел

            Dispatch(() => ApplySourceBases(summaries, cluster));
        }
        catch (RacClientException ex)
        {
            SourceError = ex.Message;
            SourceStatusText = LocalizationManager.T("ClusterMigration.Status.LoadFailed");
        }
        catch (OperationCanceledException)
        {
            SourceStatusText = LocalizationManager.T("ClusterMigration.Status.Cancelled");
        }
        catch (Exception ex)
        {
            SourceError = ex.Message;
            SourceStatusText = LocalizationManager.T("ClusterMigration.Status.LoadFailed");
            _logger?.Error("Ошибка загрузки баз кластера-источника (миграция баз)", ex);
        }
        finally
        {
            ExitBusy();
        }
    }

    /// <summary>Применяет список баз источника в UI-потоке: чеклист, ВСЕ галочки, статус источника.</summary>
    private void ApplySourceBases(IReadOnlyList<RacInfobaseSummary> summaries, RacCluster cluster)
    {
        var rows = summaries
            .Where(s => s is not null)
            .Select(s => BuildRow(s, cluster))
            .ToList();

        // «Тип СУБД»/«Сервер СУБД» (issue #41, комментарий 7OH): rac «infobase create»
        // требует непустые dbms/db-server — автозаполняем из первой базы источника, пока
        // пользователь не изменил значения вручную (_dbDetailsTouched). Пишем в поля
        // напрямую (без публичных сеттеров), чтобы сам автозаполняющий вызов не «фиксировал»
        // ввод пользователя и повторные загрузки другого кластера могли обновлять значения.
        // Имя БД в СУБД на форме отсутствует (issue #41, фикс 1.0.0.71): оно переносится
        // автоматически внутри ClusterMigrationService.BuildCreateRequest — из rac при наличии,
        // иначе из имени ИБ (некоторые версии rac не возвращают db-name в summary list,
        // см. RacOutputParser.ToInfobaseSummaries; при создании ИБ БД в СУБД по умолчанию
        // именуется так же, как ИБ).
        if (!_dbDetailsTouched)
        {
            var firstSource = rows.Select(r => r.Source).FirstOrDefault(s => s is not null);
            if (firstSource is not null)
            {
                var dbms = NormalizeDbms(firstSource.Dbms);
                if (SetProperty(ref _dbms, dbms))
                    OnPropertyChanged(nameof(Dbms));
                var dbServer = firstSource.DbServer ?? string.Empty;
                if (SetProperty(ref _dbServer, dbServer))
                    OnPropertyChanged(nameof(DbServer));
                // Локаль (issue #41, комментарий 7OH: «Поле с локалью на поле не замечено»):
                // берём из источника автоматически; rac 8.3/частичные 8.5 могут не отдавать
                // locale в summary list — тогда остаётся значение по умолчанию «ru».
                var locale = Models.RacLocales.Normalize(firstSource.Locale);
                if (SetProperty(ref _locale, locale))
                    OnPropertyChanged(nameof(Locale));
            }
        }

        SourceRows.Clear();
        foreach (var row in rows)
            SourceRows.Add(row);

        // После получения баз источника — ставим ВСЕ галочки (issue #41).
        foreach (var row in SourceRows)
            row.IsChecked = true;

        SourceStatusText = rows.Count == 0
            ? LocalizationManager.T("ClusterMigration.Error.NoBases")
            : string.Format(LocalizationManager.T("ClusterMigration.Status.LoadedBasesFormat"), rows.Count);
        NotifyCheckedState();
    }

    // ===================== Действия: приёмник =====================

    /// <summary>
    /// Загружает кластеры выбранного сервера-приёмника («cluster list»). Ошибки не роняют
    /// окно — пишутся в <see cref="TargetError"/>/<see cref="TargetStatusText"/>.
    /// </summary>
    public async Task ConnectTargetAsync()
    {
        if (!TryEnterBusy())
            return;

        IReadOnlyList<RacCluster> clusters;
        try
        {
            TargetError = string.Empty;
            TargetStatusText = LocalizationManager.T("ClusterMigration.Status.LoadingClusters");
            if (SelectedTargetServer is not { } server)
                return;

            clusters = await _rac.GetClustersAsync(BuildParams(server)).ConfigureAwait(false);
        }
        catch (RacClientException ex)
        {
            TargetError = ex.Message;
            TargetStatusText = LocalizationManager.T("ClusterMigration.Status.ConnectFailed");
            return;
        }
        catch (OperationCanceledException)
        {
            TargetStatusText = LocalizationManager.T("ClusterMigration.Status.Cancelled");
            return;
        }
        catch (Exception ex)
        {
            TargetError = ex.Message;
            TargetStatusText = LocalizationManager.T("ClusterMigration.Status.ConnectFailed");
            _logger?.Error("Ошибка загрузки кластеров сервера-приёмника (миграция баз)", ex);
            return;
        }
        finally
        {
            ExitBusy();
        }

        Dispatch(() => ApplyTargetClusters(clusters));
    }

    /// <summary>Применяет список кластеров приёмника в UI-потоке единым блоком (автовыбор единственного).</summary>
    private void ApplyTargetClusters(IReadOnlyList<RacCluster> clusters)
    {
        TargetClusters = clusters;
        SelectedTargetClusterId = null;
        TargetStatusText = clusters.Count == 0
            ? LocalizationManager.T("ClusterMigration.Status.NoClusters")
            : string.Format(LocalizationManager.T("ClusterMigration.Status.LoadedClustersFormat"), clusters.Count);
        if (clusters.Count == 1)
            SelectedTargetClusterId = clusters[0].Id;
    }

    /// <summary>
    /// «Получить статусы» (приёмник, issue #41): если базы источника загружены и выбран
    /// кластер-приёмник — загружает список баз приёмника и проставляет статусы
    /// «Есть в кластере»/пусто по совпадению имени. После получения статусов галочки
    /// остаются только там, где базы в приёмнике НЕТ.
    /// </summary>
    public async Task GetTargetStatusesAsync()
    {
        if (!TryEnterBusy())
            return;

        try
        {
            TargetError = string.Empty;
            TargetStatusText = LocalizationManager.T("ClusterMigration.Status.LoadingStatuses");
            if (SelectedTargetServer is not { } server)
                return;
            if (SelectedTargetClusterId is not Guid clusterId)
                return;
            if (SourceRows.Count == 0)
            {
                TargetStatusText = LocalizationManager.T("ClusterMigration.Error.NoSourceBases");
                return;
            }

            var bases = await _rac.GetInfobasesAsync(BuildParams(server), clusterId).ConfigureAwait(false);
            if (SelectedTargetClusterId != clusterId)
                return; // кластер сменился — результат устарел

            Dispatch(() => ApplyTargetStatuses(bases));
        }
        catch (RacClientException ex)
        {
            TargetError = ex.Message;
            TargetStatusText = LocalizationManager.T("ClusterMigration.Status.LoadFailed");
        }
        catch (OperationCanceledException)
        {
            TargetStatusText = LocalizationManager.T("ClusterMigration.Status.Cancelled");
        }
        catch (Exception ex)
        {
            TargetError = ex.Message;
            TargetStatusText = LocalizationManager.T("ClusterMigration.Status.LoadFailed");
            _logger?.Error("Ошибка получения статусов приёмника (миграция баз)", ex);
        }
        finally
        {
            ExitBusy();
        }
    }

    /// <summary>
    /// Применяет статусы приёмника в UI-потоке: «Есть в кластере»/пусто по совпадению
    /// имени; галочки остаются только там, где базы в приёмнике НЕТ (issue #41).
    /// </summary>
    private void ApplyTargetStatuses(IReadOnlyList<RacInfobaseSummary> bases)
    {
        var targetNames = new HashSet<string>(
            bases.Select(b => b.Name), StringComparer.OrdinalIgnoreCase);
        var inTargetText = LocalizationManager.T("ClusterMigration.Status.InTarget");

        foreach (var row in SourceRows)
        {
            row.TargetStatus = targetNames.Contains(row.Name) ? inTargetText : string.Empty;
            if (!string.IsNullOrEmpty(row.TargetStatus))
                row.IsChecked = false; // база уже есть в приёмнике — переносить нечего
        }

        TargetStatusText = string.Format(
            LocalizationManager.T("ClusterMigration.Status.StatusesAppliedFormat"),
            SourceRows.Count(r => !string.IsNullOrEmpty(r.TargetStatus)));
        NotifyCheckedState();
    }

    // ===================== Галочки =====================

    /// <summary>«Выделить все»: отметить все строки чеклиста.</summary>
    public void SelectAll()
    {
        foreach (var row in SourceRows)
            row.IsChecked = true;
        NotifyCheckedState();
    }

    /// <summary>«Снять все»: снять отметки со всех строк чеклиста.</summary>
    public void SelectNone()
    {
        foreach (var row in SourceRows)
            row.IsChecked = false;
        NotifyCheckedState();
    }

    /// <summary>
    /// Пересчитывает всё, что зависит от галочек/статусов чеклиста: число отмеченных,
    /// признак «отмечена база из приёмника», доступность и подсказку «Мигрировать»,
    /// сводку выделения. Вызывается и массовыми операциями, и при переключении галочки
    /// отдельной строки (см. <see cref="OnRowPropertyChanged"/>, issue #41).
    /// </summary>
    private void NotifyCheckedState()
    {
        OnPropertyChanged(nameof(CheckedCount));
        OnPropertyChanged(nameof(HasCheckedInTarget));
        OnPropertyChanged(nameof(CanMigrate));
        OnPropertyChanged(nameof(MigrateHint));
        OnPropertyChanged(nameof(SelectionSummaryText));
    }

    /// <summary>
    /// Строка чеклиста поменяла свойство <see cref="ClusterMigrationRow.IsChecked"/>
    /// (например, пользователь снял галочку вручную): пересчитываем доступность,
    /// подсказки и сводку (issue #41).
    /// </summary>
    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(ClusterMigrationRow.IsChecked), StringComparison.Ordinal))
            return;
        NotifyCheckedState();
    }

    // ===================== Миграция =====================

    /// <summary>
    /// «Мигрировать» (issue #41): для КАЖДОЙ отмеченной базы — добавить базу с теми же
    /// данными в новый кластер (rac «infobase create», данные источника + учётные данные
    /// СУБД из диалога), затем, если <see cref="KeepInSource"/> НЕ отмечен — удалить базу
    /// из старого кластера БЕЗ удаления БД на сервере СУБД (rac «infobase drop» без
    /// --drop-database). Перенесённые базы добавляются в новую группу (имя кластера —
    /// приёмник, как в импорте). Фоновый прогресс: <see cref="ProgressIndex"/> из
    /// <see cref="ProgressTotal"/>, <see cref="CurrentBaseName"/>. При успехе формирует
    /// <see cref="Result"/> (новый сервер/порт — из «cluster info» кластера приёмника)
    /// и устанавливает <see cref="MigrationCompleted"/>. Первая ошибка останавливает
    /// перенос и показывается в <see cref="ErrorMessage"/>.
    /// </summary>
    public async Task MigrateAsync()
    {
        if (!TryEnterBusy())
            return;

        try
        {
            ErrorMessage = string.Empty;

            if (ServersOverlap)
            {
                ErrorMessage = LocalizationManager.T("ClusterMigration.Error.SameServer");
                return;
            }

            if (SelectedSourceServer is not { } sourceServer)
            {
                ErrorMessage = LocalizationManager.T("ClusterMigration.Error.NoSourceServer");
                return;
            }

            if (SelectedSourceClusterId is not Guid sourceClusterId)
            {
                ErrorMessage = LocalizationManager.T("ClusterMigration.Error.NoSourceCluster");
                return;
            }

            if (SelectedTargetServer is not { } targetServer)
            {
                ErrorMessage = LocalizationManager.T("ClusterMigration.Error.NoTargetServer");
                return;
            }

            if (SelectedTargetClusterId is not Guid targetClusterId)
            {
                ErrorMessage = LocalizationManager.T("ClusterMigration.Error.NoTargetCluster");
                return;
            }

            var targetCluster = TargetClusters.FirstOrDefault(c => c.Id == targetClusterId);
            if (targetCluster is null)
            {
                ErrorMessage = LocalizationManager.T("ClusterMigration.Error.NoTargetCluster");
                return;
            }

            var rows = SourceRows.Where(r => r.IsChecked && r.Source is not null).ToList();
            if (rows.Count == 0)
            {
                ErrorMessage = LocalizationManager.T("ClusterMigration.Error.NoBases");
                return;
            }

            if (rows.Any(r => !string.IsNullOrEmpty(r.TargetStatus)))
            {
                ErrorMessage = LocalizationManager.T("ClusterMigration.Error.InTarget");
                return;
            }

            var sourceParams = BuildParams(sourceServer);
            var targetParams = BuildParams(targetServer);

            // Хост строки подключения после миграции: «cluster info» кластера приёмника
            // (корректен для удалённого RAS) → host-часть адреса сервера приёмника.
            RacClusterInfo? info = null;
            try
            {
                info = await _rac.GetClusterInfoAsync(targetParams, targetClusterId).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                info = null; // сбой «cluster info» не блокирует перенос (fallback — адрес сервера)
            }

            var newServer = ClusterMigrationService.ResolveClusterHost(targetServer.Address, info?.HostName);
            var newPort = targetCluster.Port > 0 ? targetCluster.Port : IRacClient.DefaultPort;
            var groupName = UseClusterGrouping ? (targetCluster.Name ?? string.Empty).Trim() : string.Empty;

            var migrated = new List<string>(rows.Count);
            var addedInfobases = new List<Infobase>(rows.Count);

            // Обновления прогресса доставляются в UI-поток: окно подписано на
            // PropertyChanged и напрямую трогает элементы управления (ProgressText →
            // UpdateCommandStates), а продолжение после ConfigureAwait(false) —
            // в пуле потоков (issue #41, кросс-потоковая InvalidOperationException).
            Dispatch(() => ProgressTotal = rows.Count);

            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var index = i + 1;
                var name = row.Name;
                Dispatch(() =>
                {
                    ProgressIndex = index;
                    CurrentBaseName = name;
                });

                // 1) Добавляем базу с теми же данными в новый кластер (rac «infobase create»).
                // Значения «Тип СУБД»/«Сервер СУБД» с формы (issue #41) приоритетнее данных
                // источника: приёмник может быть на другом сервере СУБД. Имя БД в СУБД
                // переносится автоматически внутри BuildCreateRequest (DbName из rac, иначе —
                // имя ИБ; issue #41, фикс 1.0.0.71), поэтому поле «Имя БД» на форме отсутствует,
                // а DbName у запроса всегда непуст (исключение — база без имени вовсе; тогда
                // rac вернёт общую ошибку создания, ключ ClusterMigration.Error.CreateFailedFormat).
                // Локаль с формы (issue #41): автозаполнена из первой базы источника; при
                // необходимости пользователь меняет списком выбора, значение передаётся
                // в запрос создания с приоритетом над данными источника.
                var request = ClusterMigrationService.BuildCreateRequest(
                    row.Source!, DbUser, DbPassword, Dbms, DbServer, Locale);
                var created = await _rac.CreateInfobaseAsync(targetParams, targetClusterId, request).ConfigureAwait(false);
                if (!created)
                {
                    ErrorMessage = string.Format(
                        LocalizationManager.T("ClusterMigration.Error.CreateFailedFormat"), row.Name, _rac.LastActionError);
                    return;
                }

                // 2) Если «Не удалять в старом» не отмечено — удаляем базу из старого
                //    кластера БЕЗ удаления БД на сервере СУБД (--drop-database не передаётся).
                if (!KeepInSource)
                {
                    var dropped = await _rac.DropInfobaseAsync(
                        sourceParams, sourceClusterId, row.Source!.InfobaseId, dropDatabase: false).ConfigureAwait(false);
                    if (!dropped)
                    {
                        ErrorMessage = string.Format(
                            LocalizationManager.T("ClusterMigration.Error.DropFailedFormat"), row.Name, _rac.LastActionError);
                        return;
                    }
                }

                // 3) База добавляется в новую группу (имя кластера — приёмник), как в импорте.
                addedInfobases.Add(RacInfobaseMapper.ToInfobase(
                    row.Source!, targetServer.Address, newPort, info?.HostName, groupName));
                migrated.Add(row.Name);
            }

            // Финальный блок (Result/MigrationCompleted) — ТОЛЬКО в UI-потоке: по
            // MigrationCompleted окно показывает диалог и закрывается (issue #41).
            var result = new ClusterMigrationResult(migrated, addedInfobases, newServer, newPort, ReplaceConnectionStrings: false);
            Dispatch(() =>
            {
                Result = result;
                StatusText = string.Format(LocalizationManager.T("ClusterMigration.SuccessFormat"), migrated.Count);
                MigrationCompleted = true;
            });
        }
        catch (OperationCanceledException)
        {
            StatusText = LocalizationManager.T("ClusterMigration.Status.Cancelled");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            StatusText = LocalizationManager.T("ClusterMigration.Status.Failed");
            _logger?.Error("Ошибка миграции баз между кластерами 1С", ex);
        }
        finally
        {
            ExitBusy();
        }
    }

    // ===================== Внутреннее =====================

    /// <summary>Строит строку чеклиста из записи базы источника (статус источника — «уже есть в списке»).</summary>
    private ClusterMigrationRow BuildRow(RacInfobaseSummary source, RacCluster cluster)
    {
        var connection = RacInfobaseMapper.ToInfobase(
            source, SelectedSourceServer?.Address ?? string.Empty, cluster.Port, null, string.Empty);
        var duplicate = RacInfobaseMapper.IsDuplicate(_existingInfobases, connection);

        var row = new ClusterMigrationRow(
            source.Name ?? string.Empty,
            BuildSubtitle(source),
            cluster.Name ?? string.Empty,
            connection.Connection?.ToConnectionString() ?? string.Empty,
            source);
        row.SourceStatus = duplicate ? LocalizationManager.T("ClusterImport.AlreadyExists") : string.Empty;
        // Переключение галочки строки вручную пересчитывает доступность/сводку (issue #41).
        row.PropertyChanged += OnRowPropertyChanged;
        return row;
    }

    /// <summary>Подпись строки: описание / тип СУБД и сервер / имя БД в СУБД (как в импорте).</summary>
    private static string BuildSubtitle(RacInfobaseSummary source)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(source.Descr))
            parts.Add(source.Descr!);
        if (!string.IsNullOrWhiteSpace(source.Dbms))
            parts.Add(source.Dbms!);
        if (!string.IsNullOrWhiteSpace(source.DbServer))
            parts.Add(source.DbServer!);
        if (!string.IsNullOrWhiteSpace(source.DbName))
            parts.Add(source.DbName!);
        return string.Join(" • ", parts);
    }

    /// <summary>
    /// Приводит тип СУБД из вывода rac к значению списка <see cref="DbmsOptions"/>
    /// (регистронезависимо); пустое/незнакомое значение → MSSQLServer (как диалог создания
    /// базы, issue #41).
    /// </summary>
    private static string NormalizeDbms(string? summaryDbms)
    {
        var value = (summaryDbms ?? string.Empty).Trim().ToLowerInvariant();
        return value switch
        {
            "mssqlserver" => RacDbms.MssqlServer,
            "postgresql" => RacDbms.PostgreSql,
            "ibmdb2" => RacDbms.IbmDb2,
            "oracledatabase" => RacDbms.OracleDatabase,
            _ => RacDbms.MssqlServer
        };
    }

    /// <summary>Запуск загрузки кластеров источника без ожидания (fire-and-forget).</summary>
    private void StartConnectSource() => _ = ConnectSourceAsync();

    /// <summary>Запуск загрузки баз источника без ожидания (fire-and-forget).</summary>
    private void StartLoadSourceBases(Guid clusterId) => _ = LoadSourceBasesAsync(clusterId);

    /// <summary>Запуск загрузки кластеров приёмника без ожидания (fire-and-forget).</summary>
    private void StartConnectTarget() => _ = ConnectTargetAsync();

    /// <summary>Запуск «Получить статусы» без ожидания (fire-and-forget).</summary>
    private void StartGetTargetStatuses(Guid clusterId) => _ = GetTargetStatusesAsync();

    /// <summary>Параметры подключения к серверу из записи справочника
    /// (адрес «host[:port]» с приоритетом порта из адреса, issue #324).</summary>
    private static RacConnectionParams BuildParams(ManagementServer server)
    {
        var (host, port) = RacConnectionAddress.Split(server.Address, server.Port);
        return new RacConnectionParams
        {
            Address = host,
            Port = port,
            User = server.User?.Trim() ?? string.Empty,
            Password = server.Password ?? string.Empty
        };
    }

    /// <summary>Доставляет действие в UI-поток (или выполняет сразу в тестах).</summary>
    private void Dispatch(Action action)
    {
        if (_dispatchToUi is null)
        {
            action();
            return;
        }
        try
        {
            _dispatchToUi(action);
        }
        catch
        {
            action();
        }
    }

    private bool TryEnterBusy()
    {
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

    /// <summary>Доставляет уведомление IsBusy в UI-поток (паттерн окна импорта, issue #25).</summary>
    private void NotifyIsBusy()
    {
        if (_dispatchToUi is null)
        {
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(CanMigrate));
            OnPropertyChanged(nameof(MigrateHint));
            return;
        }
        _dispatchToUi(() =>
        {
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(CanMigrate));
            OnPropertyChanged(nameof(MigrateHint));
        });
    }
}
