using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты вью-модели окна «Миграция между кластерами 1С» (issue #41, переработка по
/// комментарию 7OH): двухпанельная раскладка «Источник»/«Приёмник» (сервера не
/// пересекаются — проверка по выбору, из списков не удаляются), загрузка кластеров
/// с автовыбором единственного, «Получить базы» (ВСЕ галочки), «Получить статусы»
/// («Есть в кластере»/пусто по совпадению имени; галочки остаются только там, где
/// базы нет в приёмнике), доступность «Мигрировать» по галочкам и статусам, перенос
/// (create в приёмнике + drop из источника при снятой «Не удалять в старом»), добавление
/// баз в новую группу (имя кластера — приёмник) и формирование результата.
/// </summary>
public sealed class ClusterMigrationViewModelTests
{
    [Fact]
    public void Ctor_KeepsAllServersInBothPanels()
    {
        var source = BuildServer("src", "srv-old");
        var target = BuildServer("tgt", "srv-new");

        var vm = new ClusterMigrationViewModel(
            new FakeRacClient(), Array.Empty<Infobase>(),
            new ManagementServer[] { source, target });

        // Сервера НЕ удаляются из списков (issue #41): оба сервера в обеих панелях.
        Assert.Equal(2, vm.AvailableSourceServers.Count);
        Assert.Equal(2, vm.AvailableTargetServers.Count);
        Assert.True(vm.HasAvailableServers);
        Assert.False(vm.IsBusy);
        Assert.False(vm.MigrationCompleted);
        Assert.Null(vm.Result);
        Assert.False(vm.ServersOverlap);
    }

    [Fact]
    public async Task SourceServerChange_LoadsClusters_SingleClusterAutoSelected()
    {
        var client = new FakeRacClient(singleCluster: true);
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old") });

        vm.SelectedSourceServer = vm.AvailableSourceServers[0];

        await AwaitSourceClusters(vm);

        Assert.Single(vm.SourceClusters);
        Assert.Equal(FakeRacClient.SourceClusterId, vm.SelectedSourceClusterId); // автовыбор единственного
        Assert.Equal("srv-old", client.LastParams!.Address);
        Assert.Empty(vm.SourceError);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task TargetServerChange_LoadsClusters_SingleClusterAutoSelected()
    {
        var client = new FakeRacClient(singleCluster: true);
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("tgt", "srv-new") });

        vm.SelectedTargetServer = vm.AvailableTargetServers[0];

        await AwaitTargetClusters(vm);

        Assert.Single(vm.TargetClusters);
        Assert.Equal(FakeRacClient.TargetClusterId, vm.SelectedTargetClusterId);
        Assert.Equal("srv-new", client.LastParams!.Address);
        Assert.Empty(vm.TargetError);
    }

    [Fact]
    public async Task LoadSourceBases_BuildsRows_ChecksAll()
    {
        var client = new FakeRacClient(singleCluster: true);
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);

        await AwaitSourceRows(vm);

        Assert.Equal(2, vm.SourceRows.Count);
        // После получения баз источника — ВСЕ галочки (issue #41).
        Assert.All(vm.SourceRows, r => Assert.True(r.IsChecked));
        Assert.Equal(2, vm.CheckedCount);
        Assert.False(vm.HasCheckedInTarget);
        Assert.Equal(
            string.Format(LocalizationManager.T("ClusterMigration.Status.LoadedBasesFormat"), 2),
            vm.SourceStatusText);
    }

    [Fact]
    public async Task LoadSourceBases_MarksDuplicatesInSourceStatus()
    {
        var existing = new Infobase
        {
            Name = "База1",
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.ClientServer,
                Server = "srv-old",
                Port = 1542, // порт кластера-источника из FakeRacClient
                DatabaseName = "База1"
            }
        };
        var client = new FakeRacClient(singleCluster: true);
        var vm = new ClusterMigrationViewModel(
            client, new[] { existing }, new[] { BuildServer("src", "srv-old") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);

        await AwaitSourceRows(vm);

        var row = vm.SourceRows.First(r => r.Name == "База1");
        Assert.Equal(LocalizationManager.T("ClusterImport.AlreadyExists"), row.SourceStatus);
        // Статус источника не мешает переносу — галочка стоит.
        Assert.True(row.IsChecked);
    }

    [Fact]
    public async Task GetTargetStatuses_SetsInTarget_UnchecksExisting()
    {
        var client = new FakeRacClient(singleCluster: true, targetBaseNames: new[] { "База1" });
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old"), BuildServer("tgt", "srv-new") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);
        await AwaitSourceRows(vm);
        Assert.Equal(2, vm.CheckedCount);

        vm.SelectedTargetServer = vm.AvailableTargetServers[1];
        await AwaitTargetClusters(vm);

        await vm.GetTargetStatusesAsync();

        // «Есть в кластере» по совпадению имени; галочка снята у существующей в приёмнике.
        var inTarget = vm.SourceRows.First(r => r.Name == "База1");
        var free = vm.SourceRows.First(r => r.Name == "База2");
        Assert.Equal(LocalizationManager.T("ClusterMigration.Status.InTarget"), inTarget.TargetStatus);
        Assert.Equal(string.Empty, free.TargetStatus);
        Assert.False(inTarget.IsChecked);
        Assert.True(free.IsChecked);
        Assert.Equal(1, vm.CheckedCount);
        // Отмеченных строк со статусом «Есть в кластере» нет — «Мигрировать» доступна.
        Assert.False(vm.HasCheckedInTarget);
        Assert.True(vm.CanMigrate);

        // Пользователь вручную отмечает базу, которая есть в приёмнике — кнопка недоступна.
        inTarget.IsChecked = true;
        Assert.True(vm.HasCheckedInTarget);
        Assert.False(vm.CanMigrate);
        Assert.NotEqual(string.Empty, vm.MigrateHint);
    }

    [Fact]
    public async Task GetTargetStatuses_NoMatches_KeepsAllChecked()
    {
        var client = new FakeRacClient(singleCluster: true, targetBaseNames: new[] { "СовсемДругаяБаза" });
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old"), BuildServer("tgt", "srv-new") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);
        await AwaitSourceRows(vm);

        vm.SelectedTargetServer = vm.AvailableTargetServers[1];
        await AwaitTargetClusters(vm);
        await vm.GetTargetStatusesAsync();

        Assert.All(vm.SourceRows, r => Assert.Equal(string.Empty, r.TargetStatus));
        Assert.All(vm.SourceRows, r => Assert.True(r.IsChecked));
        Assert.False(vm.HasCheckedInTarget);
    }

    [Fact]
    public void ServersOverlap_WhenSameServerSelectedInBothPanels()
    {
        var server = BuildServer("srv", "srv-1");
        var vm = new ClusterMigrationViewModel(
            new FakeRacClient(), Array.Empty<Infobase>(), new[] { server });

        vm.SelectedSourceServer = server;
        vm.SelectedTargetServer = server;

        Assert.True(vm.ServersOverlap);
        Assert.False(vm.CanMigrate);
        Assert.NotEqual(string.Empty, vm.MigrateHint);
    }

    [Fact]
    public async Task Migrate_CreatesInTarget_DropsFromSource_AddsToGroup()
    {
        var client = new FakeRacClient();
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old"), BuildServer("tgt", "srv-new") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);
        await AwaitSourceRows(vm);

        vm.SelectedTargetServer = vm.AvailableTargetServers[1];
        await AwaitTargetClusters(vm);
        await vm.GetTargetStatusesAsync();

        vm.DbUser = "db-user";
        vm.DbPassword = "db-pass";
        vm.KeepInSource = false;
        vm.UseClusterGrouping = true;

        await vm.MigrateAsync();

        Assert.True(vm.MigrationCompleted);
        Assert.Empty(vm.ErrorMessage);
        Assert.False(vm.IsBusy);
        Assert.Equal(2, vm.Result!.MigratedBaseNames.Count);
        Assert.Equal(new[] { "База1", "База2" }, vm.Result.MigratedBaseNames);
        Assert.Equal("srv-new-host", vm.Result.NewServer);   // hostName из «cluster info»
        Assert.Equal(1542, vm.Result.NewPort);               // порт кластера приёмника

        // Базы добавляются в новую группу (имя кластера — приёмник), как в импорте.
        Assert.Equal(2, vm.Result.AddedInfobases.Count);
        Assert.All(vm.Result.AddedInfobases, b => Assert.Equal("Новый кластер", b.Group));
        Assert.All(vm.Result.AddedInfobases, b => Assert.Equal("srv-new-host", b.Connection!.Server));

        // Каждая база: create в приёмнике + drop из источника (dropDatabase=false).
        Assert.Equal(2, client.Created.Count);
        Assert.Equal(2, client.Dropped.Count);
        Assert.All(client.Created, c => Assert.Equal(FakeRacClient.TargetClusterId, c.ClusterId));
        Assert.All(client.Dropped, d => Assert.False(d.DropDatabase));
        Assert.Equal("db-user", client.Created[0].Request.DbUser);
        Assert.Equal("db-pass", client.Created[0].Request.DbPassword);
        Assert.Equal("MSSQLServer", client.Created[0].Request.Dbms);
    }

    [Fact]
    public async Task Migrate_KeepInSource_CreateOnly_NoDrop()
    {
        var client = new FakeRacClient();
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old"), BuildServer("tgt", "srv-new") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);
        await AwaitSourceRows(vm);

        vm.SelectedTargetServer = vm.AvailableTargetServers[1];
        await AwaitTargetClusters(vm);
        await vm.GetTargetStatusesAsync();

        vm.KeepInSource = true; // «Не удалять в старом» — копия без удаления

        await vm.MigrateAsync();

        Assert.True(vm.MigrationCompleted);
        Assert.Equal(2, client.Created.Count); // обе базы источника копируются
        Assert.Empty(client.Dropped);
    }

    [Fact]
    public async Task Migrate_NoGrouping_AddsBasesWithoutGroup()
    {
        var client = new FakeRacClient();
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old"), BuildServer("tgt", "srv-new") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);
        await AwaitSourceRows(vm);

        vm.SelectedTargetServer = vm.AvailableTargetServers[1];
        await AwaitTargetClusters(vm);
        await vm.GetTargetStatusesAsync();

        vm.UseClusterGrouping = false;

        await vm.MigrateAsync();

        Assert.True(vm.MigrationCompleted);
        Assert.All(vm.Result!.AddedInfobases, b => Assert.Equal(string.Empty, b.Group));
    }

    [Fact]
    public async Task Migrate_Progress_UpdatesIndexTotalCurrentBase()
    {
        var client = new FakeRacClient();
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old"), BuildServer("tgt", "srv-new") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);
        await AwaitSourceRows(vm);

        vm.SelectedTargetServer = vm.AvailableTargetServers[1];
        await AwaitTargetClusters(vm);
        await vm.GetTargetStatusesAsync();

        await vm.MigrateAsync();

        Assert.Equal(2, vm.ProgressTotal);
        Assert.Equal(2, vm.ProgressIndex);
        Assert.Equal("База2", vm.CurrentBaseName);
        Assert.NotEmpty(vm.ProgressText);
    }

    [Fact]
    public async Task Migrate_CreateFailure_SetsError_Stops()
    {
        var client = new FakeRacClient(failCreate: true);
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old"), BuildServer("tgt", "srv-new") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);
        await AwaitSourceRows(vm);

        vm.SelectedTargetServer = vm.AvailableTargetServers[1];
        await AwaitTargetClusters(vm);
        await vm.GetTargetStatusesAsync();

        await vm.MigrateAsync();

        Assert.False(vm.MigrationCompleted);
        Assert.Null(vm.Result);
        Assert.NotEmpty(vm.ErrorMessage);
        Assert.False(vm.IsBusy);
        Assert.Empty(client.Dropped);
        Assert.Single(client.Created);
    }

    [Fact]
    public async Task Migrate_DropFailure_SetsError()
    {
        var client = new FakeRacClient(failDrop: true);
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old"), BuildServer("tgt", "srv-new") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);
        await AwaitSourceRows(vm);

        vm.SelectedTargetServer = vm.AvailableTargetServers[1];
        await AwaitTargetClusters(vm);
        await vm.GetTargetStatusesAsync();

        await vm.MigrateAsync();

        Assert.False(vm.MigrationCompleted);
        Assert.NotEmpty(vm.ErrorMessage);
        Assert.Single(client.Created);
        Assert.Single(client.Dropped);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Migrate_SameServer_Validates()
    {
        var server = BuildServer("srv", "srv-1");
        var vm = new ClusterMigrationViewModel(
            new FakeRacClient(), Array.Empty<Infobase>(), new[] { server });
        vm.SelectedSourceServer = server;
        vm.SelectedTargetServer = server;

        await vm.MigrateAsync();

        Assert.False(vm.MigrationCompleted);
        Assert.NotEmpty(vm.ErrorMessage);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Migrate_NoSourceServer_Validates()
    {
        var vm = new ClusterMigrationViewModel(
            new FakeRacClient(), Array.Empty<Infobase>(), Array.Empty<ManagementServer>());

        await vm.MigrateAsync();

        Assert.False(vm.MigrationCompleted);
        Assert.NotEmpty(vm.ErrorMessage);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Migrate_NoTargetCluster_Validates()
    {
        var client = new FakeRacClient(singleCluster: false); // несколько кластеров — автовыбора нет
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old"), BuildServer("tgt", "srv-new") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);
        await AwaitSourceRows(vm);

        vm.SelectedTargetServer = vm.AvailableTargetServers[1];
        await AwaitTargetClusters(vm);
        Assert.Null(vm.SelectedTargetClusterId);

        await vm.MigrateAsync();

        Assert.False(vm.MigrationCompleted);
        Assert.NotEmpty(vm.ErrorMessage);
        Assert.Empty(client.Created);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Migrate_NoBases_Validates()
    {
        var vm = new ClusterMigrationViewModel(
            new FakeRacClient(), Array.Empty<Infobase>(), Array.Empty<ManagementServer>());

        await vm.MigrateAsync();

        Assert.False(vm.MigrationCompleted);
        Assert.NotEmpty(vm.ErrorMessage);
        Assert.Equal(0, vm.CheckedCount);
    }

    [Fact]
    public async Task SetReplaceConnectionStrings_UpdatesResultFlag()
    {
        var client = new FakeRacClient();
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old"), BuildServer("tgt", "srv-new") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);
        await AwaitSourceRows(vm);

        vm.SelectedTargetServer = vm.AvailableTargetServers[1];
        await AwaitTargetClusters(vm);
        await vm.GetTargetStatusesAsync();

        await vm.MigrateAsync();

        Assert.True(vm.MigrationCompleted);
        Assert.False(vm.Result!.ReplaceConnectionStrings);
        vm.SetReplaceConnectionStrings(true);
        Assert.True(vm.Result.ReplaceConnectionStrings);
        vm.SetReplaceConnectionStrings(false);
        Assert.False(vm.Result.ReplaceConnectionStrings);
    }

    [Fact]
    public async Task SelectAll_SelectNone_ToggleChecks()
    {
        var client = new FakeRacClient();
        var vm = new ClusterMigrationViewModel(
            client, Array.Empty<Infobase>(), new[] { BuildServer("src", "srv-old") });
        vm.SelectedSourceServer = vm.AvailableSourceServers[0];
        await AwaitSourceClusters(vm);
        await AwaitSourceRows(vm);

        vm.SelectNone();
        Assert.Equal(0, vm.CheckedCount);
        Assert.False(vm.CanMigrate);

        vm.SelectAll();
        Assert.Equal(2, vm.CheckedCount);
    }

    // ===================== Хелперы =====================

    private static async Task AwaitSourceClusters(ClusterMigrationViewModel vm)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (vm.SourceClusters.Count == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(10);
    }

    private static async Task AwaitSourceRows(ClusterMigrationViewModel vm)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (vm.SourceRows.Count == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(10);
    }

    private static async Task AwaitTargetClusters(ClusterMigrationViewModel vm)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (vm.TargetClusters.Count == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(10);
    }

    private static ManagementServer BuildServer(string id, string address) => new()
    {
        Id = id,
        Description = "Сервер " + address,
        Address = address,
        Port = 1545,
        User = "admin",
        Password = "secret"
    };

    // ===================== Fake =====================

    /// <summary>
    /// Fake-клиент rac для тестов миграции: кластеры источника/приёмника (один по умолчанию,
    /// несколько по запросу), «cluster info» с hostName, базы источника/приёмника, подсчёт
    /// и запись вызовов «infobase create»/«infobase drop» (с параметрами) и имитация сбоев.
    /// </summary>
    private sealed class FakeRacClient : IRacClient
    {
        public static readonly Guid SourceClusterId = Guid.Parse("99000000-0000-0000-0000-000000000001");
        public static readonly Guid TargetClusterId = Guid.Parse("88000000-0000-0000-0000-000000000002");
        public static readonly Guid TargetCluster2Id = Guid.Parse("77000000-0000-0000-0000-000000000003");

        private readonly bool _singleCluster;
        private readonly bool _failCreate;
        private readonly bool _failDrop;
        private readonly string[] _targetBaseNames;

        public FakeRacClient(bool singleCluster = true, bool failCreate = false, bool failDrop = false,
            string[]? targetBaseNames = null)
        {
            _singleCluster = singleCluster;
            _failCreate = failCreate;
            _failDrop = failDrop;
            _targetBaseNames = targetBaseNames ?? Array.Empty<string>();
        }

        public RacConnectionParams? LastParams { get; private set; }

        public List<(Guid ClusterId, RacInfobaseCreateRequest Request)> Created { get; } = new();
        public List<(Guid ClusterId, Guid InfobaseId, bool DropDatabase)> Dropped { get; } = new();

        public string LastActionError { get; private set; } = string.Empty;

        public Task<IReadOnlyList<RacCluster>> GetClustersAsync(
            RacConnectionParams parameters, CancellationToken cancellationToken = default)
        {
            LastParams = parameters;
            // Сервер-источник (srv-old) отдаёт кластер-источник, приёмник — кластер-приёмник.
            var isSource = string.Equals(parameters.Address, "srv-old", StringComparison.OrdinalIgnoreCase);
            var clusterId = isSource ? SourceClusterId : TargetClusterId;
            IReadOnlyList<RacCluster> clusters = _singleCluster
                ? new RacCluster[] { new() { Id = clusterId, Name = "Новый кластер", Port = 1542 } }
                : new RacCluster[]
                {
                    new() { Id = clusterId, Name = "Новый кластер", Port = 1542 },
                    new() { Id = TargetCluster2Id, Name = "Второй новый", Port = 2542 }
                };
            return Task.FromResult(clusters);
        }

        public Task<RacClusterInfo?> GetClusterInfoAsync(
            RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default) =>
            Task.FromResult<RacClusterInfo?>(new RacClusterInfo
            {
                Name = "Новый кластер",
                HostName = "srv-new-host",
                Port = clusterId == TargetClusterId ? 1542 : 2542
            });

        public Task<IReadOnlyList<RacInfobaseSummary>> GetInfobasesAsync(
            RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default)
        {
            // Источник: две базы; приёмник: заданные имена (по умолчанию — пусто).
            if (clusterId == SourceClusterId)
            {
                return Task.FromResult<IReadOnlyList<RacInfobaseSummary>>(new RacInfobaseSummary[]
                {
                    new() { InfobaseId = Guid.Parse("66000000-0000-0000-0000-000000000001"), Name = "База1", Descr = "Описание", Dbms = "MSSQLServer", DbServer = "sql-01", DbName = "база1" },
                    new() { InfobaseId = Guid.Parse("66000000-0000-0000-0000-000000000002"), Name = "База2", Descr = "Описание", Dbms = "MSSQLServer", DbServer = "sql-01", DbName = "база2" }
                });
            }
            return Task.FromResult<IReadOnlyList<RacInfobaseSummary>>(
                _targetBaseNames.Select(name => new RacInfobaseSummary { Name = name }).ToList());
        }

        public Task<bool> CreateInfobaseAsync(
            RacConnectionParams parameters, Guid clusterId, RacInfobaseCreateRequest request,
            CancellationToken cancellationToken = default)
        {
            Created.Add((clusterId, request));
            if (_failCreate)
            {
                LastActionError = "нет прав на создание базы (имитация)";
                return Task.FromResult(false);
            }
            LastActionError = string.Empty;
            return Task.FromResult(true);
        }

        public Task<bool> DropInfobaseAsync(
            RacConnectionParams parameters, Guid clusterId, Guid infobaseId,
            bool dropDatabase = false, CancellationToken cancellationToken = default)
        {
            Dropped.Add((clusterId, infobaseId, dropDatabase));
            if (_failDrop)
            {
                LastActionError = "нет прав на удаление базы (имитация)";
                return Task.FromResult(false);
            }
            LastActionError = string.Empty;
            return Task.FromResult(true);
        }

        // ---- Неиспользуемые методы интерфейса ----

        public Task<bool> UpdateClusterAsync(
            RacConnectionParams parameters, Guid clusterId, RacClusterUpdate changes,
            CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<IReadOnlyList<RacProcessInfo>> GetProcessesAsync(
            RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RacProcessInfo>>(Array.Empty<RacProcessInfo>());

        public Task<IReadOnlyList<RacSessionInfo>> GetSessionsAsync(
            RacConnectionParams parameters, Guid clusterId, Guid? infobaseId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RacSessionInfo>>(Array.Empty<RacSessionInfo>());

        public Task<IReadOnlyList<RacConnectionInfo>> GetConnectionsAsync(
            RacConnectionParams parameters, Guid clusterId, Guid? infobaseId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RacConnectionInfo>>(Array.Empty<RacConnectionInfo>());

        public Task<IReadOnlyList<RacLockInfo>> GetLocksAsync(
            RacConnectionParams parameters, Guid clusterId, Guid? infobaseId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RacLockInfo>>(Array.Empty<RacLockInfo>());

        public Task<IReadOnlyList<RacJobInfo>> GetJobsAsync(
            RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RacJobInfo>>(Array.Empty<RacJobInfo>());

        public Task<bool> SetJobStateAsync(
            RacConnectionParams parameters, Guid clusterId, Guid jobId, RacJobAction action,
            CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> TerminateSessionAsync(
            RacConnectionParams parameters, Guid clusterId, Guid sessionId,
            CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> DisconnectConnectionAsync(
            RacConnectionParams parameters, Guid clusterId, Guid connectionId,
            CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> UpdateInfobaseAsync(
            RacConnectionParams parameters, Guid clusterId, Guid infobaseId,
            RacInfobaseUpdateRequest request, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
