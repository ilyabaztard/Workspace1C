using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты чистого планировщика массовой замены в строках подключения (0.3.9.188, функция 6
/// «Массовая замена в строке подключения баз»): отбор кандидатов области, построение плана
/// предпросмотра без мутаций, применение с формированием снапшота для отката и
/// восстановление через Undo.
/// </summary>
public sealed class ConnectionReplacementPlannerTests
{
    // ---------- Хелперы ----------

    private static Infobase ServerBase(
        string id,
        string name,
        string server = "server1",
        string @ref = "База",
        int port = 1541,
        string group = "",
        string user = "",
        string password = "",
        bool blockScheduledJobs = false,
        bool forbidSpeechRecognition = false)
        => new()
        {
            Id = id,
            Name = name,
            Group = group,
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.ClientServer,
                Server = server,
                DatabaseName = @ref,
                Port = port,
                User = user,
                Password = password,
                AuthenticationMode = string.IsNullOrEmpty(user)
                    ? AuthenticationMode.Prompt
                    : AuthenticationMode.Credentials,
                BlockScheduledJobs = blockScheduledJobs,
                ForbidSpeechRecognition = forbidSpeechRecognition
            }
        };

    private static Infobase FileBase(string id, string name, string path, string group = "")
        => new()
        {
            Id = id,
            Name = name,
            Group = group,
            Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = path }
        };

    private static Infobase WebBase(string id, string name, string url, string group = "")
        => new()
        {
            Id = id,
            Name = name,
            Group = group,
            Connection = new ConnectionSettings { Type = ConnectionType.WebServer, WebUrl = url }
        };

    /// <summary>База с дефолтными (пустыми) настройками подключения — «без подключения».</summary>
    private static Infobase EmptyBase(string id, string name, string group = "")
        => new() { Id = id, Name = name, Group = group };

    private static ConnectionStringReplaceRule Rule(
        string find,
        string replace,
        ConnectionField field = ConnectionField.Server,
        ConnectionMatchMode mode = ConnectionMatchMode.Exact,
        bool ignoreCase = false)
        => new(find, replace, field, mode, ignoreCase);

    private static ConnectionSettings CloneSettings(ConnectionSettings source) => new()
    {
        Type = source.Type,
        Server = source.Server,
        DatabaseName = source.DatabaseName,
        FilePath = source.FilePath,
        BlockScheduledJobs = source.BlockScheduledJobs,
        ForbidSpeechRecognition = source.ForbidSpeechRecognition,
        User = source.User,
        Password = source.Password,
        AuthenticationMode = source.AuthenticationMode,
        Port = source.Port,
        WebUrl = source.WebUrl
    };

    private static void AssertConnectionEqual(ConnectionSettings expected, ConnectionSettings actual)
    {
        Assert.Equal(expected.Type, actual.Type);
        Assert.Equal(expected.Server, actual.Server);
        Assert.Equal(expected.DatabaseName, actual.DatabaseName);
        Assert.Equal(expected.FilePath, actual.FilePath);
        Assert.Equal(expected.BlockScheduledJobs, actual.BlockScheduledJobs);
        Assert.Equal(expected.ForbidSpeechRecognition, actual.ForbidSpeechRecognition);
        Assert.Equal(expected.User, actual.User);
        Assert.Equal(expected.Password, actual.Password);
        Assert.Equal(expected.AuthenticationMode, actual.AuthenticationMode);
        Assert.Equal(expected.Port, actual.Port);
        Assert.Equal(expected.WebUrl, actual.WebUrl);
    }

    // ---------- SelectCandidates: области ----------

    [Fact]
    public void SelectCandidates_AllBases_ReturnsWholeVisibleList()
    {
        var list = new[]
        {
            ServerBase("1", "А"),
            ServerBase("2", "Б"),
            FileBase("3", "В", @"C:\b")
        };

        var result = ConnectionReplacementPlanner.SelectCandidates(
            list, null, null, ConnectionReplaceScope.AllBases);

        Assert.Equal(new[] { "1", "2", "3" }, result.Select(b => b.Id));
    }

    [Fact]
    public void SelectCandidates_BatchSelected_OnlyById_InListOrder()
    {
        var list = new[]
        {
            ServerBase("1", "А"),
            ServerBase("2", "Б"),
            ServerBase("3", "В")
        };

        // Порядок результата — как в списке, а не как в наборе Id.
        var result = ConnectionReplacementPlanner.SelectCandidates(
            list, new[] { "3", "1" }, null, ConnectionReplaceScope.BatchSelected);

        Assert.Equal(new[] { "1", "3" }, result.Select(b => b.Id));
    }

    [Fact]
    public void SelectCandidates_BatchSelected_UnknownIdsIgnored()
    {
        var list = new[]
        {
            ServerBase("1", "А"),
            ServerBase("2", "Б")
        };

        var result = ConnectionReplacementPlanner.SelectCandidates(
            list, new[] { "1", "нет-такого", "999" }, null, ConnectionReplaceScope.BatchSelected);

        Assert.Equal(new[] { "1" }, result.Select(b => b.Id));
    }

    [Fact]
    public void SelectCandidates_BatchSelected_EmptyOrNullSet_ReturnsEmpty()
    {
        var list = new[] { ServerBase("1", "А") };

        Assert.Empty(ConnectionReplacementPlanner.SelectCandidates(
            list, Array.Empty<string>(), null, ConnectionReplaceScope.BatchSelected));
        Assert.Empty(ConnectionReplacementPlanner.SelectCandidates(
            list, null, null, ConnectionReplaceScope.BatchSelected));
    }

    [Fact]
    public void SelectCandidates_CurrentGroup_OnlyBasesOfGroup()
    {
        var list = new[]
        {
            ServerBase("1", "А", group: "Рабочие"),
            FileBase("2", "Б", @"C:\b", group: "Рабочие"),
            ServerBase("3", "В", group: "Архив"),
            EmptyBase("4", "Г", group: "Рабочие")
        };

        var result = ConnectionReplacementPlanner.SelectCandidates(
            list, null, "Рабочие", ConnectionReplaceScope.CurrentGroup);

        Assert.Equal(new[] { "1", "2", "4" }, result.Select(b => b.Id));
    }

    [Fact]
    public void SelectCandidates_CurrentGroup_EmptyOrNullGroup_ReturnsEmpty()
    {
        var list = new[] { ServerBase("1", "А", group: "Рабочие") };

        Assert.Empty(ConnectionReplacementPlanner.SelectCandidates(
            list, null, "", ConnectionReplaceScope.CurrentGroup));
        Assert.Empty(ConnectionReplacementPlanner.SelectCandidates(
            list, null, null, ConnectionReplaceScope.CurrentGroup));
    }

    [Fact]
    public void SelectCandidates_CurrentGroup_UnknownGroup_ReturnsEmpty()
    {
        var list = new[] { ServerBase("1", "А", group: "Рабочие") };

        Assert.Empty(ConnectionReplacementPlanner.SelectCandidates(
            list, null, "Нет такой группы", ConnectionReplaceScope.CurrentGroup));
    }

    // ---------- Plan: строки, счётчики, отсутствие мутаций ----------

    [Fact]
    public void Plan_RowsOnlyChangedBases_CountersCorrect()
    {
        var changed1 = ServerBase("1", "А", server: "server1");
        var changed2 = ServerBase("2", "Б", server: "server1");
        var noMatch = ServerBase("3", "В", server: "other");
        var empty = EmptyBase("4", "Г");

        var plan = ConnectionReplacementPlanner.Plan(
            new[] { changed1, changed2, noMatch, empty },
            Rule("server1", "server2"));

        Assert.Equal(2, plan.Rows.Count);
        Assert.All(plan.Rows, r => Assert.True(r.Changed));
        Assert.Equal(2, plan.AffectedCount);
        Assert.Equal(1, plan.NoMatchCount);
        Assert.Equal(1, plan.EmptyConnectionCount);
    }

    [Fact]
    public void Plan_RowCarriesBaseFieldBeforeAfter()
    {
        var b = ServerBase("1", "А", server: "server1");

        var plan = ConnectionReplacementPlanner.Plan(new[] { b }, Rule("server1", "server2"));

        var row = Assert.Single(plan.Rows);
        Assert.Same(b, row.Infobase);
        Assert.Equal(ConnectionField.Server, row.Field);
        Assert.Equal("server1", row.BeforeText);
        Assert.Equal("server2", row.AfterText);
    }

    [Fact]
    public void Plan_AnyField_BeforeAfterAreWholeStrings()
    {
        var b = ServerBase("1", "А", server: "server1");

        var plan = ConnectionReplacementPlanner.Plan(
            new[] { b },
            Rule("server1", "server2", ConnectionField.Any, ConnectionMatchMode.Substring));

        var row = Assert.Single(plan.Rows);
        Assert.Equal(@"Srvr=""server1"";Ref=""База""", row.BeforeText);
        Assert.Equal(@"Srvr=""server2"";Ref=""База""", row.AfterText);
    }

    [Fact]
    public void Plan_PortField_BeforeAfterAreNumericStrings()
    {
        var b = ServerBase("1", "А", server: "server1", port: 1541);

        var plan = ConnectionReplacementPlanner.Plan(
            new[] { b },
            Rule("1541", "2541", ConnectionField.Port));

        var row = Assert.Single(plan.Rows);
        Assert.Equal("1541", row.BeforeText);
        Assert.Equal("2541", row.AfterText);
    }

    [Fact]
    public void Plan_AffectedCount_CountsUniqueBases()
    {
        // Одна и та же база дважды в списке кандидатов: Rows — 2, AffectedCount — 1.
        var b = ServerBase("1", "А", server: "server1");

        var plan = ConnectionReplacementPlanner.Plan(new[] { b, b }, Rule("server1", "server2"));

        Assert.Equal(2, plan.Rows.Count);
        Assert.Equal(1, plan.AffectedCount);
    }

    [Fact]
    public void Plan_DoesNotMutateSourceBases()
    {
        var b1 = ServerBase("1", "А", server: "server1", port: 2541, user: "user", password: "pass");
        var b2 = FileBase("2", "Б", @"C:\base");

        var before1 = b1.Connection.ToConnectionString();
        var before2 = b2.Connection.ToConnectionString();

        ConnectionReplacementPlanner.Plan(new[] { b1, b2 }, Rule("server1", "server2"));

        Assert.Equal(before1, b1.Connection.ToConnectionString());
        Assert.Equal(before2, b2.Connection.ToConnectionString());
    }

    [Fact]
    public void Plan_IgnoreCase_MatchesAndReplaces()
    {
        var b = ServerBase("1", "А", server: "SERVER1");

        var plan = ConnectionReplacementPlanner.Plan(
            new[] { b },
            Rule("server1", "server2", ignoreCase: true));

        var row = Assert.Single(plan.Rows);
        Assert.Equal("SERVER1", row.BeforeText);
        Assert.Equal("server2", row.AfterText);
    }

    [Fact]
    public void Plan_FieldNotApplicableToBaseType_NoMatch()
    {
        // Поле FilePath неприменимо к серверной базе — «без совпадений».
        var serverBase = ServerBase("1", "Серверная", server: "server1");

        var plan = ConnectionReplacementPlanner.Plan(
            new[] { serverBase },
            Rule(@"C:\old", @"D:\new", ConnectionField.FilePath, ConnectionMatchMode.Prefix));

        Assert.Empty(plan.Rows);
        Assert.Equal(0, plan.AffectedCount);
        Assert.Equal(1, plan.NoMatchCount);
        Assert.Equal(0, plan.EmptyConnectionCount);
    }

    [Fact]
    public void Plan_FileBase_MatchingPath_Produced()
    {
        var fileBase = FileBase("1", "Файловая", @"C:\old\base");
        var serverBase = ServerBase("2", "Серверная", server: "server1");

        var plan = ConnectionReplacementPlanner.Plan(
            new[] { fileBase, serverBase },
            Rule(@"C:\old", @"D:\new", ConnectionField.FilePath, ConnectionMatchMode.Prefix));

        var row = Assert.Single(plan.Rows);
        Assert.Equal("1", row.Infobase.Id);
        Assert.Equal(@"C:\old\base", row.BeforeText);
        Assert.Equal(@"D:\new\base", row.AfterText);
        Assert.Equal(1, plan.NoMatchCount); // серверная база — без совпадений
    }

    [Fact]
    public void Plan_WebBase_UrlReplacement()
    {
        var webBase = WebBase("1", "Веб", "http://old.example.com/base");

        var plan = ConnectionReplacementPlanner.Plan(
            new[] { webBase },
            Rule("old.example.com", "new.example.com", ConnectionField.WebUrl, ConnectionMatchMode.Substring));

        var row = Assert.Single(plan.Rows);
        Assert.Equal("http://old.example.com/base", row.BeforeText);
        Assert.Equal("http://new.example.com/base", row.AfterText);
    }

    // ---------- Plan: крайние случаи ----------

    [Fact]
    public void Plan_NoMatchAtAll_EmptyRows_NoMatchCountIsCandidates()
    {
        var list = new[]
        {
            ServerBase("1", "А", server: "srvA"),
            ServerBase("2", "Б", server: "srvB")
        };

        var plan = ConnectionReplacementPlanner.Plan(list, Rule("srvX", "srvY"));

        Assert.Empty(plan.Rows);
        Assert.Equal(0, plan.AffectedCount);
        Assert.Equal(2, plan.NoMatchCount);
        Assert.Equal(0, plan.EmptyConnectionCount);
    }

    [Fact]
    public void Plan_EmptyCandidateList()
    {
        var plan = ConnectionReplacementPlanner.Plan(Array.Empty<Infobase>(), Rule("a", "b"));

        Assert.Empty(plan.Rows);
        Assert.Equal(0, plan.AffectedCount);
        Assert.Equal(0, plan.NoMatchCount);
        Assert.Equal(0, plan.EmptyConnectionCount);
    }

    [Fact]
    public void Plan_DefaultEmptyConnection_CountedAsEmpty_NotInRows()
    {
        var empty = EmptyBase("1", "Пустая");
        var normal = ServerBase("2", "Норм", server: "server1");

        var plan = ConnectionReplacementPlanner.Plan(new[] { empty, normal }, Rule("server1", "server2"));

        Assert.Single(plan.Rows);
        Assert.Equal(1, plan.AffectedCount);
        Assert.Equal(0, plan.NoMatchCount);
        Assert.Equal(1, plan.EmptyConnectionCount);
        Assert.DoesNotContain(plan.Rows, r => r.Infobase.Id == "1");
    }

    [Fact]
    public void Plan_EmptyFind_NoMatchForAllCandidates()
    {
        var b = ServerBase("1", "А", server: "server1");

        var plan = ConnectionReplacementPlanner.Plan(new[] { b }, Rule("", "x"));

        Assert.Empty(plan.Rows);
        Assert.Equal(1, plan.NoMatchCount);
        Assert.Equal(0, plan.EmptyConnectionCount);
    }

    [Fact]
    public void Plan_InvalidRegex_ThrowsArgumentException()
    {
        var b = ServerBase("1", "А", server: "server1");

        Assert.Throws<ArgumentException>(() =>
            ConnectionReplacementPlanner.Plan(new[] { b }, Rule("[", "x", mode: ConnectionMatchMode.Regex)));
    }

    // ---------- Apply: мутация, глубокие копии, After ----------

    [Fact]
    public void Apply_MutatesOnlyAffectedBases()
    {
        var affected = ServerBase("1", "А", server: "server1");
        var noMatch = ServerBase("2", "Б", server: "server3");
        var untouched = ServerBase("3", "В", server: "other");

        var entries = ConnectionReplacementPlanner.Apply(
            new[] { affected, noMatch, untouched },
            Rule("server1", "server2"));

        Assert.Single(entries);
        Assert.Equal(@"Srvr=""server2"";Ref=""База""", affected.Connection.ToConnectionString());
        Assert.Equal(@"Srvr=""server3"";Ref=""База""", noMatch.Connection.ToConnectionString());
        Assert.Equal(@"Srvr=""other"";Ref=""База""", untouched.Connection.ToConnectionString());
    }

    [Fact]
    public void Apply_SkipsEmptyConnectionBases()
    {
        var b = ServerBase("1", "А", server: "server1");
        var empty = EmptyBase("2", "Пустая");

        var entries = ConnectionReplacementPlanner.Apply(new[] { b, empty }, Rule("server1", "server2"));

        Assert.Single(entries);
        // Пустая база не тронута — каноническая строка осталась вырожденной.
        Assert.Equal(@"Srvr="""";Ref=""""", empty.Connection.ToConnectionString());
    }

    [Fact]
    public void Apply_NoMatch_ReturnsEmptyAndMutatesNothing()
    {
        var b = ServerBase("1", "А", server: "server1");
        var before = b.Connection.ToConnectionString();

        var entries = ConnectionReplacementPlanner.Apply(new[] { b }, Rule("srvX", "srvY"));

        Assert.Empty(entries);
        Assert.Equal(before, b.Connection.ToConnectionString());
    }

    [Fact]
    public void Apply_EmptyCandidateList_ReturnsEmpty()
    {
        Assert.Empty(ConnectionReplacementPlanner.Apply(Array.Empty<Infobase>(), Rule("a", "b")));
    }

    [Fact]
    public void Apply_Before_IsDeepCopy_UnaffectedByLaterEdits()
    {
        var b1 = ServerBase("1", "А", server: "server1", port: 2541, user: "u1", password: "p1");
        var b2 = ServerBase("2", "Б", server: "server1", port: 2541, user: "u2", password: "p2");

        var entries = ConnectionReplacementPlanner.Apply(new[] { b1, b2 }, Rule("server1", "server2"));
        Assert.Equal(2, entries.Count);

        // Правки после применения (в том числе у другой базы) не влияют на снапшоты.
        b1.Connection.Server = "перезаписан";
        b2.Connection.User = "перезаписан";
        b2.Connection.Password = "перезаписан";

        var e1 = entries.Single(e => e.Infobase == b1);
        var e2 = entries.Single(e => e.Infobase == b2);
        Assert.Equal("server1", e1.Before.Server);
        Assert.Equal("u1", e1.Before.User);
        Assert.Equal("p1", e1.Before.Password);
        Assert.Equal(2541, e1.Before.Port);
        Assert.Equal("server2", e1.After.Server);
        Assert.Equal("server1", e2.Before.Server);
        Assert.Equal("u2", e2.Before.User);
        Assert.Equal("p2", e2.Before.Password);
    }

    [Fact]
    public void Apply_Before_CopiesAllConnectionFields()
    {
        var b = ServerBase(
            "1", "А", server: "server1", @ref: "База", port: 2541,
            user: "user", password: "pass", blockScheduledJobs: true, forbidSpeechRecognition: true);

        var entries = ConnectionReplacementPlanner.Apply(new[] { b }, Rule("server1", "server2"));
        var entry = Assert.Single(entries);

        Assert.Equal(ConnectionType.ClientServer, entry.Before.Type);
        Assert.Equal("server1", entry.Before.Server);
        Assert.Equal("База", entry.Before.DatabaseName);
        Assert.Equal(string.Empty, entry.Before.FilePath);
        Assert.True(entry.Before.BlockScheduledJobs);
        Assert.True(entry.Before.ForbidSpeechRecognition);
        Assert.Equal("user", entry.Before.User);
        Assert.Equal("pass", entry.Before.Password);
        Assert.Equal(AuthenticationMode.Credentials, entry.Before.AuthenticationMode);
        Assert.Equal(2541, entry.Before.Port);
        Assert.Equal(string.Empty, entry.Before.WebUrl);
    }

    [Fact]
    public void Apply_After_MatchesExpectedNewConnectionString()
    {
        var b = ServerBase("1", "А", server: "server1", @ref: "База");

        var entries = ConnectionReplacementPlanner.Apply(new[] { b }, Rule("server1", "server2"));
        var entry = Assert.Single(entries);

        Assert.Equal(@"Srvr=""server2"";Ref=""База""", entry.After.ToConnectionString());
        Assert.Equal(@"Srvr=""server2"";Ref=""База""", b.Connection.ToConnectionString());
    }

    [Fact]
    public void Apply_UndoEntry_ReferencesSameBase()
    {
        var b = ServerBase("1", "А", server: "server1");

        var entries = ConnectionReplacementPlanner.Apply(new[] { b }, Rule("server1", "server2"));

        Assert.Same(b, Assert.Single(entries).Infobase);
    }

    // ---------- Undo: восстановление ----------

    [Fact]
    public void Undo_RestoresFullConnectionEquality()
    {
        var b = ServerBase(
            "1", "А", server: "server1", @ref: "База", port: 2541,
            user: "user", password: "pass", blockScheduledJobs: true, forbidSpeechRecognition: true);
        var original = CloneSettings(b.Connection);

        var entries = ConnectionReplacementPlanner.Apply(new[] { b }, Rule("server1", "server2"));
        Assert.NotEqual(original.ToConnectionString(), b.Connection.ToConnectionString());

        // Дополнительная порча состояния до отката.
        b.Connection.User = "изменён после применения";

        ConnectionReplacementPlanner.Undo(entries);

        AssertConnectionEqual(original, b.Connection);
        Assert.Equal(original.ToConnectionString(), b.Connection.ToConnectionString());
    }

    [Fact]
    public void Undo_MultipleEntries_RestoresEach()
    {
        var b1 = ServerBase("1", "А", server: "server1");
        var b2 = ServerBase("2", "Б", server: "server1");

        var entries = ConnectionReplacementPlanner.Apply(new[] { b1, b2 }, Rule("server1", "server2"));
        Assert.Equal(2, entries.Count);

        ConnectionReplacementPlanner.Undo(entries);

        Assert.Equal(@"Srvr=""server1"";Ref=""База""", b1.Connection.ToConnectionString());
        Assert.Equal(@"Srvr=""server1"";Ref=""База""", b2.Connection.ToConnectionString());
    }

    [Fact]
    public void Undo_EmptyOrNullEntries_NoOp()
    {
        ConnectionReplacementPlanner.Undo(Array.Empty<ConnectionReplaceUndoEntry>());
        ConnectionReplacementPlanner.Undo(null!);
    }

    // ---------- Issue #54: смена платформы ----------

    [Fact]
    public void PlanPlatform_RowsOnlyBasesWithDifferentPlatform()
    {
        var same = ServerBase("1", "А");
        same.PlatformVersion = "8.3.24.1664";
        var different = ServerBase("2", "Б");
        different.PlatformVersion = "8.3.22.1483";
        var emptyPlatform = ServerBase("3", "В");

        var rows = ConnectionReplacementPlanner.PlanPlatform(
            new[] { same, different, emptyPlatform },
            new PlatformReplaceRule("8.3.24.1664"));

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.True(r.IsPlatformChange));
        Assert.All(rows, r => Assert.True(r.Changed));
        var row2 = rows.Single(r => r.Infobase.Id == "2");
        Assert.Equal("8.3.22.1483", row2.BeforeText);
        Assert.Equal("8.3.24.1664", row2.AfterText);
        Assert.Contains(rows, r => r.Infobase.Id == "3");
        // Без мутаций.
        Assert.Equal("8.3.22.1483", different.PlatformVersion);
    }

    [Fact]
    public void PlanPlatform_IgnoresSameVersion_IgnoreCaseAndWhitespace()
    {
        var b = ServerBase("1", "А");
        b.PlatformVersion = "8.3.24.1664";

        var rows = ConnectionReplacementPlanner.PlanPlatform(new[] { b }, new PlatformReplaceRule("8.3.24.1664 "));

        Assert.Empty(rows);
    }

    [Fact]
    public void PlanPlatform_EmptyPlatformRule_ReturnsEmpty()
    {
        var b = ServerBase("1", "А");

        Assert.Empty(ConnectionReplacementPlanner.PlanPlatform(new[] { b }, new PlatformReplaceRule("  ")));
    }

    [Fact]
    public void ApplyPlatform_MutatesVersion_CreatesUndoEntriesWithBeforeAfterPlatform()
    {
        var b = ServerBase("1", "А");
        b.PlatformVersion = "8.3.22.1483";
        var same = ServerBase("2", "Б");
        same.PlatformVersion = "8.3.24.1664";

        var entries = ConnectionReplacementPlanner.ApplyPlatform(
            new[] { b, same },
            new PlatformReplaceRule("8.3.24.1664"));

        var entry = Assert.Single(entries);
        Assert.Same(b, entry.Infobase);
        Assert.Equal("8.3.22.1483", entry.BeforePlatform);
        Assert.Equal("8.3.24.1664", entry.AfterPlatform);
        Assert.Equal("8.3.24.1664", b.PlatformVersion);
        Assert.Equal("8.3.24.1664", same.PlatformVersion); // уже совпадала — без изменений
        // Строка подключения записью не меняется: Before/After — копии одних и тех же настроек.
        AssertConnectionEqual(entry.Before, entry.After);
    }

    [Fact]
    public void Undo_RestoresPlatformVersion()
    {
        var b = ServerBase("1", "А");
        b.PlatformVersion = "8.3.22.1483";

        var entries = ConnectionReplacementPlanner.ApplyPlatform(new[] { b }, new PlatformReplaceRule("8.3.24.1664"));
        Assert.Equal("8.3.24.1664", b.PlatformVersion);

        ConnectionReplacementPlanner.Undo(entries);

        Assert.Equal("8.3.22.1483", b.PlatformVersion);
    }

    [Fact]
    public void MergeUndoEntries_CombinesConnectionAndPlatformForSameBase()
    {
        var b = ServerBase("1", "А", server: "server1");
        b.PlatformVersion = "8.3.22.1483";
        var c = ServerBase("2", "Б", server: "server1");

        var stringEntries = ConnectionReplacementPlanner.Apply(new[] { b, c }, Rule("server1", "server2"));
        var platformEntries = ConnectionReplacementPlanner.ApplyPlatform(new[] { b }, new PlatformReplaceRule("8.3.24.1664"));

        var merged = ConnectionReplacementPlanner.MergeUndoEntries(stringEntries.Concat(platformEntries));

        Assert.Equal(2, merged.Count);
        var eB = merged.Single(e => e.Infobase == b);
        Assert.Equal("server1", eB.Before.Server);
        Assert.Equal("server2", eB.After.Server);
        Assert.Equal("8.3.22.1483", eB.BeforePlatform);
        Assert.Equal("8.3.24.1664", eB.AfterPlatform);
        var eC = merged.Single(e => e.Infobase == c);
        Assert.Equal("server2", eC.After.Server);
        Assert.Null(eC.BeforePlatform);
        Assert.Null(eC.AfterPlatform);
    }

    [Fact]
    public void MergeUndoEntries_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Empty(ConnectionReplacementPlanner.MergeUndoEntries(null));
        Assert.Empty(ConnectionReplacementPlanner.MergeUndoEntries(Array.Empty<ConnectionReplaceUndoEntry>()));
    }
}