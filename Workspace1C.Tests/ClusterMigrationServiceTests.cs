using System;
using System.Collections.Generic;
using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты чистой логики миграции баз между кластерами 1С (1.0.0.51, issue #41):
/// построение запроса «infobase create» «с теми же данными, что в старом кластере»,
/// исключение сервера-источника из списка назначения, замена сервера/порта в строках
/// подключения баз списка приложения и разрешение хоста кластера для строки подключения.
/// </summary>
public sealed class ClusterMigrationServiceTests
{
    // ---------- BuildCreateRequest ----------

    [Fact]
    public void BuildCreateRequest_MapsAllFields_FromSourceSummary()
    {
        var source = new RacInfobaseSummary
        {
            InfobaseId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Name = "Бухгалтерия",
            Descr = "Основная база",
            Dbms = "MSSQLServer",
            DbServer = "sql-01",
            DbName = "buho",
            DbUser = "old-user",          // rac возвращает пользователя СУБД — но в запрос идёт из диалога
            Locale = "ru_RU",
            SecurityLevel = 1
        };

        var request = ClusterMigrationService.BuildCreateRequest(source, "new-user", "pass");

        Assert.Equal("Бухгалтерия", request.Name);
        Assert.Equal("Основная база", request.Description);
        Assert.Equal("MSSQLServer", request.Dbms);
        Assert.Equal("sql-01", request.DbServer);
        Assert.Equal("buho", request.DbName);
        Assert.Equal("new-user", request.DbUser);
        Assert.Equal("pass", request.DbPassword);
        Assert.Equal("ru_RU", request.Locale);
        Assert.Equal(1, request.SecurityLevel);
    }

    [Fact]
    public void BuildCreateRequest_CreateDbFalse_ScheduledJobsDenyFalse()
    {
        // БД на сервере СУБД уже существует (старый кластер подключался к ней): переносим
        // ТОЛЬКО регистрацию базы в новом кластере — «--create-database» не передаётся.
        var request = ClusterMigrationService.BuildCreateRequest(
            new RacInfobaseSummary(), string.Empty, string.Empty);

        Assert.False(request.CreateDb);
        Assert.False(request.ScheduledJobsDeny);
        // Пустые учётные данные диалога → пустые поля (пустая строка, не null).
        Assert.Equal(string.Empty, request.DbUser);
        Assert.Equal(string.Empty, request.DbPassword);
    }

    [Fact]
    public void BuildCreateRequest_UsesOverrideDbmsAndDbServer_WhenProvided()
    {
        // issue #41, комментарий 7OH: rac «infobase create» требует тип СУБД и сервер СУБД;
        // для приёмника на другом сервере СУБД значения из формы миграции приоритетнее
        // значений источника (иначе — «странная ошибка» создания базы).
        var source = new RacInfobaseSummary
        {
            Name = "Бухгалтерия",
            Dbms = "MSSQLServer",
            DbServer = "sql-01",
            DbName = "buho"
        };

        var request = ClusterMigrationService.BuildCreateRequest(
            source, "new-user", "pass", "PostgreSQL", "pg-02");

        Assert.Equal("PostgreSQL", request.Dbms);
        Assert.Equal("pg-02", request.DbServer);
        // Остальные поля по-прежнему из источника.
        Assert.Equal("Бухгалтерия", request.Name);
        Assert.Equal("buho", request.DbName);
    }

    [Fact]
    public void BuildCreateRequest_FallsBackToSourceDbmsAndDbServer_WhenOverridesEmpty()
    {
        // Пустые/пробельные значения формы — используются значения источника (тест на
        // сохранение прежнего поведения при вызове без override-параметров).
        var source = new RacInfobaseSummary
        {
            Dbms = "OracleDatabase",
            DbServer = "ora-01"
        };

        var request = ClusterMigrationService.BuildCreateRequest(source, "", "", "", "   ");

        Assert.Equal("OracleDatabase", request.Dbms);
        Assert.Equal("ora-01", request.DbServer);
    }

    // ---------- ExcludeSourceServer ----------

    [Fact]
    public void ExcludeSourceServer_RemovesById_KeepsOthers()
    {
        var source = BuildServer("src", "Старый сервер");
        var other = BuildServer("tgt", "Новый сервер");
        var list = new ManagementServer[] { source, other };

        var result = ClusterMigrationService.ExcludeSourceServer(list, source);

        Assert.Single(result);
        Assert.Same(other, result[0]);
    }

    [Fact]
    public void ExcludeSourceServer_SourceIdCaseInsensitive()
    {
        var source = BuildServer("SRC-ABC", "Старый сервер");
        var clone = BuildServer("src-abc", "Тот же Id другим регистром");

        var result = ClusterMigrationService.ExcludeSourceServer(new[] { source, clone }, source);

        Assert.Empty(result); // оба «совпадают» по Id без учёта регистра
    }

    [Fact]
    public void ExcludeSourceServer_NullSource_KeepsAll()
    {
        var a = BuildServer("a", "А");
        var b = BuildServer("b", "Б");

        var result = ClusterMigrationService.ExcludeSourceServer(new[] { a, b }, null);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void ExcludeSourceServer_NullInput_ReturnsEmpty()
    {
        Assert.Empty(ClusterMigrationService.ExcludeSourceServer(null, null));
    }

    // ---------- ReplaceServerAndPort ----------

    [Fact]
    public void ReplaceServerAndPort_ReplacesMatchingClientServerBases_ReturnsCount()
    {
        var bases = new[]
        {
            ServerBase("1", "Бухгалтерия", "srv-old", "Бухгалтерия", 1541),
            ServerBase("2", "Зарплата", "srv-old", "Зарплата", 1541),
            ServerBase("3", "Другое имя", "srv-old", "Другое имя", 1541)
        };

        var replaced = ClusterMigrationService.ReplaceServerAndPort(
            bases, new[] { "Бухгалтерия", "Зарплата" }, "srv-new", 1542);

        Assert.Equal(2, replaced);
        Assert.Equal("srv-new", bases[0].Connection.Server);
        Assert.Equal(1542, bases[0].Connection.Port);
        Assert.Equal("srv-new", bases[1].Connection.Server);
        Assert.Equal(1542, bases[1].Connection.Port);
        Assert.Equal("srv-old", bases[2].Connection.Server); // не совпала — не тронута
        Assert.Equal(1541, bases[2].Connection.Port);
    }

    [Fact]
    public void ReplaceServerAndPort_CaseInsensitiveNameMatch()
    {
        var target = ServerBase("1", "Бухгалтерия", "srv-old", "бухгалтерия", 1541);

        var replaced = ClusterMigrationService.ReplaceServerAndPort(
            new[] { target }, new[] { "БУХГАЛТЕРИЯ" }, "srv-new", 1542);

        Assert.Equal(1, replaced);
        Assert.Equal("srv-new", target.Connection.Server);
    }

    [Fact]
    public void ReplaceServerAndPort_SkipsFileBases_AndNonClientServer()
    {
        var fileBase = new Infobase
        {
            Id = "1",
            Name = "Бухгалтерия",
            Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"C:\base" }
        };
        var webBase = new Infobase
        {
            Id = "2",
            Name = "Бухгалтерия",
            Connection = new ConnectionSettings { Type = ConnectionType.WebServer, WebUrl = "http://s/base" }
        };

        var replaced = ClusterMigrationService.ReplaceServerAndPort(
            new[] { fileBase, webBase }, new[] { "Бухгалтерия" }, "srv-new", 1542);

        Assert.Equal(0, replaced);
    }

    [Fact]
    public void ReplaceServerAndPort_EmptyNames_OrEmptyServer_NoOp()
    {
        var base1 = ServerBase("1", "Бухгалтерия", "srv-old", "Бухгалтерия", 1541);

        Assert.Equal(0, ClusterMigrationService.ReplaceServerAndPort(
            new[] { base1 }, Array.Empty<string>(), "srv-new", 1542));
        Assert.Equal(0, ClusterMigrationService.ReplaceServerAndPort(
            new[] { base1 }, new[] { "Бухгалтерия" }, "   ", 1542));
        Assert.Equal("srv-old", base1.Connection.Server);
    }

    [Fact]
    public void ReplaceServerAndPort_NullInputs_NoOp()
    {
        Assert.Equal(0, ClusterMigrationService.ReplaceServerAndPort(null, new[] { "x" }, "s", 1));
        Assert.Equal(0, ClusterMigrationService.ReplaceServerAndPort(
            new List<Infobase>(), null, "s", 1));
    }

    // ---------- ResolveClusterHost ----------

    [Fact]
    public void ResolveClusterHost_ClusterHostNameWins()
    {
        Assert.Equal("srv5", ClusterMigrationService.ResolveClusterHost("ras-proxy:1545", "srv5"));
    }

    [Fact]
    public void ResolveClusterHost_FallbackToServerAddress_HostPart()
    {
        // Порт RAS (1545) в строку подключения клиентов не попадает — только хост.
        Assert.Equal("srv5", ClusterMigrationService.ResolveClusterHost("srv5:1545", null));
        Assert.Equal("srv5", ClusterMigrationService.ResolveClusterHost("srv5", ""));
    }

    [Fact]
    public void ResolveClusterHost_EmptyAddress_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, ClusterMigrationService.ResolveClusterHost("   ", null));
    }

    // ---------- Хелперы ----------

    /// <summary>Запись справочника «Серверы 1С» для тестов.</summary>
    private static ManagementServer BuildServer(string id, string description) => new()
    {
        Id = id,
        Description = description,
        Address = "srv-" + id,
        Port = 1545,
        User = "admin",
        Password = "secret"
    };

    /// <summary>Клиент-серверная база списка приложения.</summary>
    private static Infobase ServerBase(
        string id, string name, string server, string @ref, int port) => new()
    {
        Id = id,
        Name = name,
        Connection = new ConnectionSettings
        {
            Type = ConnectionType.ClientServer,
            Server = server,
            DatabaseName = @ref,
            Port = port
        }
    };
}
