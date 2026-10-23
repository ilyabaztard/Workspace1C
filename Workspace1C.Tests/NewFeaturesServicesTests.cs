using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Чистая логика функций выпуска 1.0.0.69–1.0.0.69: Git-версионирование,
/// технологический журнал, веб-публикация, мониторинг, копии СУБД, команды
/// Telegram-бота и общие списки баз.
/// </summary>
public sealed class NewFeaturesServicesTests
{
    // ---------------- Git (1.0.0.69) ----------------

    [Fact]
    public void Git_FormatMessage_ReplacesPlaceholders()
    {
        var ib = new Infobase { Name = "ЗУП", ConfigurationVersion = "3.1.30.1" };
        var text = GitVersioningService.FormatMessage("{base} {version} {date}", ib, new DateTime(2026, 10, 23, 9, 5, 0));
        Assert.Equal("ЗУП 3.1.30.1 23.10.2026 09:05", text);
    }

    [Theory]
    [InlineData("src", true)]
    [InlineData("config/main", true)]
    [InlineData("..", false)]
    [InlineData("../other", false)]
    [InlineData(".git", false)]
    public void Git_ResolveDumpDirectory_StaysInsideRepository(string sub, bool ok)
    {
        var repo = Path.Combine(Path.GetTempPath(), "repo-" + Guid.NewGuid().ToString("N"));
        var settings = new GitRepoSettings { RepoPath = repo, SubFolder = sub };
        if (ok)
            Assert.StartsWith(Path.GetFullPath(repo), GitVersioningService.ResolveDumpDirectory(settings));
        else
            Assert.ThrowsAny<Exception>(() => GitVersioningService.ResolveDumpDirectory(settings));
    }

    [Fact]
    public void Git_DataUpsertReplacesByInfobase()
    {
        var data = new GitVersioningData();
        data.Upsert(new GitRepoSettings { InfobaseId = "a", RepoPath = "1" });
        data.Upsert(new GitRepoSettings { InfobaseId = "a", RepoPath = "2" });
        Assert.Single(data.Repos);
        Assert.Equal("2", data.Find("a")!.RepoPath);
        Assert.Null(data.Find("b"));
    }

    // ---------------- Технологический журнал (1.0.0.69) ----------------

    [Fact]
    public void TechLog_SlowQueries_UsesMicroseconds()
    {
        var xml = TechLogService.BuildXml(TechLogTemplate.SlowQueries, "C:\\logs", 24, 500);
        Assert.Contains("value=\"DBMSSQL\"", xml);
        Assert.Contains("value=\"DBPOSTGRS\"", xml);
        Assert.Contains("<gt property=\"duration\" value=\"500000\"/>", xml);
        Assert.Contains("history=\"24\"", xml);
        Assert.Contains("location=\"C:\\logs\"", xml);
        System.Xml.Linq.XDocument.Parse(xml);
    }

    [Fact]
    public void TechLog_EscapesLocationAndClampsHistory()
    {
        var xml = TechLogService.BuildXml(TechLogTemplate.Exceptions, "C:\\a&b", 5000, 1);
        Assert.Contains("C:\\a&amp;b", xml);
        Assert.Contains("history=\"720\"", xml);
        Assert.Contains("value=\"EXCP\"", xml);
        System.Xml.Linq.XDocument.Parse(xml);
    }

    [Fact]
    public void TechLog_EnableAndDisable_KeepBackups()
    {
        var dir = Path.Combine(Path.GetTempPath(), "conf-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Null(TechLogService.Enable(dir, "<config/>"));
            Assert.Null(TechLogService.Enable(dir, "<config2/>"));
            Assert.Equal("<config2/>", TechLogService.ReadCurrent(dir));
            Assert.Single(Directory.GetFiles(dir, "logcfg.xml.bak-*"));
            Assert.Null(TechLogService.Disable(dir));
            Assert.Equal(string.Empty, TechLogService.ReadCurrent(dir));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    // ---------------- Веб-публикация (1.0.0.69) ----------------

    [Theory]
    [InlineData("Бухгалтерия предприятия", "buhgalteriya_predpriyatiya")]
    [InlineData("ЗУП 3.1", "zup_3_1")]
    [InlineData("!!!", "base")]
    public void WebPublish_SuggestVirtualDirectory(string name, string expected)
    {
        Assert.Equal(expected, WebPublishService.SuggestVirtualDirectory(name));
    }

    [Fact]
    public void WebPublish_Arguments()
    {
        var request = new WebPublishRequest
        {
            Server = WebServerKind.Apache24,
            VirtualDirectory = "zup",
            PhysicalDirectory = "/var/www/zup",
            ApacheConfPath = "/etc/apache2/apache2.conf",
            ConnectionString = "Srvr=\"srv\";Ref=\"zup\";"
        };
        Assert.Null(WebPublishService.Validate(request));
        var args = WebPublishService.BuildPublishArguments(request);
        Assert.Equal(new[] { "-publish", "-apache24", "-wsdir", "zup", "-dir", "/var/www/zup", "-connstr",
            "Srvr=\"srv\";Ref=\"zup\";", "-confpath", "/etc/apache2/apache2.conf" }, args);

        request.Server = WebServerKind.Iis;
        var delete = WebPublishService.BuildDeleteArguments(request);
        Assert.Equal(new[] { "-delete", "-iis", "-wsdir", "zup", "-dir", "/var/www/zup" }, delete);
    }

    [Fact]
    public void WebPublish_ValidateRejectsBadName()
    {
        var request = new WebPublishRequest { VirtualDirectory = "a b", PhysicalDirectory = "x", ConnectionString = "File=x;" };
        Assert.Equal("WebPublish.BadName", WebPublishService.Validate(request));
    }

    // ---------------- Мониторинг (1.0.0.69) ----------------

    [Fact]
    public void Alerts_EvaluateThresholds()
    {
        var rule = new ServerAlertRule { MaxSessions = 10, MaxProcessMemoryMb = 1000, MaxWaitingLocks = 1 };
        var processes = new[]
        {
            new RacProcessInfo { Host = "srv", Pid = 1, MemorySize = 2_000_000 }, // ~1953 МБ
            new RacProcessInfo { Host = "srv", Pid = 2, MemorySize = 100_000 }
        };
        var locks = new[] { new RacLockInfo { Waiting = true }, new RacLockInfo { Waiting = true }, new RacLockInfo() };
        var alerts = ServerAlertsService.Evaluate(rule, "srv", 11, processes, locks);
        Assert.Equal(3, alerts.Count);

        var none = ServerAlertsService.Evaluate(new ServerAlertRule(), "srv", 1000, processes, locks);
        Assert.Empty(none);
    }

    [Fact]
    public void NotificationFilter_MonitoringRespectsSetting()
    {
        var settings = new NotificationChannelSettings();
        var message = new NotificationMessage { Event = NotificationEvent.Monitoring, Kind = NotificationKind.Warning };
        Assert.True(NotificationEventFilter.ShouldSend(settings, message));
        settings.NotifyOnMonitoring = false;
        Assert.False(NotificationEventFilter.ShouldSend(settings, message));
    }

    // ---------------- Копии СУБД (1.0.0.69) ----------------

    [Fact]
    public void Dbms_PgDumpArguments()
    {
        var profile = new DbmsBackupProfile { Host = "pg", Port = 5433, User = "postgres", Database = "zup" };
        var args = DbmsBackupService.BuildPgDumpArguments(profile, "/b/zup.backup");
        Assert.Equal(new[] { "-h", "pg", "-p", "5433", "-U", "postgres", "-F", "c", "-b", "-w", "-f", "/b/zup.backup", "zup" }, args);
    }

    [Fact]
    public void Dbms_MsSqlQueryEscapes()
    {
        var profile = new DbmsBackupProfile { Dbms = DbmsKind.MsSql, Host = "sql", Database = "a]b" };
        var query = DbmsBackupService.BuildMsSqlBackupQuery(profile, "D:\\O'Brien\\x.bak");
        Assert.Equal("BACKUP DATABASE [a]]b] TO DISK = N'D:\\O''Brien\\x.bak' WITH COPY_ONLY, INIT, CHECKSUM, STATS = 10", query);
        var args = DbmsBackupService.BuildSqlCmdArguments(profile, "x.bak");
        Assert.Contains("-E", args);
    }

    [Fact]
    public void Dbms_FileNameAndPrefix()
    {
        var profile = new DbmsBackupProfile { Database = "zup prod" };
        Assert.Equal("zup_prod_", DbmsBackupService.BuildFilePrefix(profile));
        Assert.Equal("zup_prod_2026-10-23_101500.backup",
            DbmsBackupService.BuildFileName(profile, new DateTime(2026, 10, 23, 10, 15, 0)));
        Assert.Equal("Dbms.DirRequired", DbmsBackupService.Validate(new DbmsBackupProfile { Database = "x" }));
    }

    [Fact]
    public void WebDav_CombineUrlEscapesFileName()
    {
        Assert.Equal("https://webdav.yandex.ru/b/a%20b.bak", WebDavUploader.CombineUrl("https://webdav.yandex.ru/b/", "a b.bak"));
    }

    // ---------------- Telegram-бот (1.0.0.69) ----------------

    [Theory]
    [InlineData("/run@MyBot 2", "/run", "2")]
    [InlineData("  /STATUS ", "/status", "")]
    [InlineData("hello", "", "")]
    public void Bot_SplitCommand(string text, string command, string argument)
    {
        Assert.Equal((command, argument), TelegramBotCommandService.SplitCommand(text));
    }

    [Fact]
    public void Bot_ParseUpdates()
    {
        const string json = "{\"ok\":true,\"result\":[{\"update_id\":7,\"message\":{\"chat\":{\"id\":-100123},\"text\":\"/tasks\"}},{\"update_id\":8,\"callback_query\":{}}]}";
        var updates = TelegramBotCommandService.ParseUpdates(json);
        Assert.Equal(2, updates.Count);
        Assert.Equal("-100123", updates[0].ChatId);
        Assert.Equal("/tasks", updates[0].Text);
        Assert.Equal(string.Empty, updates[1].ChatId);
        Assert.Empty(TelegramBotCommandService.ParseUpdates("not json"));
    }

    [Fact]
    public void Bot_AllowedChats_FallbackToNotificationChat()
    {
        Assert.Equal(new[] { "42" }, TelegramBotCommandService.AllowedChats(new TelegramBotSettings(), "42").ToArray());
        var explicitChats = TelegramBotCommandService.AllowedChats(new TelegramBotSettings { AllowedChatIds = "1, 2;3" }, "42");
        Assert.Equal(3, explicitChats.Count);
        Assert.DoesNotContain("42", explicitChats);
    }

    [Fact]
    public void Bot_FormatTasks_NumbersFromOne()
    {
        var tasks = new List<ScheduledTask>
        {
            new() { Name = "Копия", LastRunAt = new DateTime(2026, 10, 23, 2, 0, 0), LastRunSuccess = true },
            new() { Name = "Обновление", Enabled = false }
        };
        var text = TelegramBotCommandService.FormatTasks(tasks);
        Assert.StartsWith("1. Копия", text);
        Assert.Contains("2. Обновление", text);
    }

    // ---------------- Общие списки (1.0.0.69) ----------------

    private static Infobase FileBase(string name, string path, string id = "", string group = "") => new()
    {
        Id = id,
        Name = name,
        Group = group,
        Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = path }
    };

    [Fact]
    public void Shared_MergeAddsUpdatesAndRemoves()
    {
        var personal = FileBase("Моя база", "C:\\my", "personal-id");
        var infobases = new List<Infobase> { personal };
        var groups = new List<Group>();

        var first = SharedListService.Merge(infobases, groups, "Отдел", new[]
        {
            FileBase("Бухгалтерия", "\\\\srv\\bp", "bp", "Учёт"),
            FileBase("ЗУП", "\\\\srv\\zup", "zup"),
            FileBase("Дубль личной", "C:\\my", "personal-id")
        });
        Assert.Equal(2, first.Added);
        Assert.Equal(1, first.Skipped);
        Assert.Equal(3, infobases.Count);
        var bp = infobases.Single(b => b.Id == "bp");
        Assert.Equal("Отдел", bp.SharedSource);
        Assert.Equal("Отдел" + GroupHierarchyHelper.PathSeparator + "Учёт", bp.Group);
        Assert.NotNull(GroupHierarchyHelper.FindByFullPath(bp.Group, groups));

        var second = SharedListService.Merge(infobases, groups, "Отдел", new[]
        {
            FileBase("Бухгалтерия 3.0", "\\\\srv\\bp", "bp", "Учёт")
        });
        Assert.Equal(1, second.Updated);
        Assert.Equal(1, second.Removed);
        Assert.Contains(personal, infobases);
        Assert.Equal("Бухгалтерия 3.0", infobases.Single(b => b.Id == "bp").Name);
        Assert.DoesNotContain(infobases, b => b.Id == "zup");
    }

    [Fact]
    public void Shared_EnsureGroupPath_CreatesChainOnce()
    {
        var groups = new List<Group>();
        var path = "A" + GroupHierarchyHelper.PathSeparator + "B";
        SharedListService.EnsureGroupPath(groups, path);
        SharedListService.EnsureGroupPath(groups, path);
        Assert.Equal(2, groups.Count);
        Assert.Equal(groups[0].Id, groups[1].ParentId);
    }

    // ---------------- Хранилище настроек ----------------

    [Fact]
    public void FeatureDataStore_RoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fds-" + Guid.NewGuid().ToString("N"));
        var previous = FeatureDataStore.DirectoryOverride;
        FeatureDataStore.DirectoryOverride = dir;
        try
        {
            Assert.Empty(FeatureDataStore.Load<SharedListSettings>("x.json").Sources);
            FeatureDataStore.Save("x.json", new SharedListSettings
            {
                IntervalMinutes = 5,
                Sources = { new SharedListSource { Name = "Отдел", Location = "\\\\srv\\ibases.v8i" } }
            });
            var loaded = FeatureDataStore.Load<SharedListSettings>("x.json");
            Assert.Equal(5, loaded.IntervalMinutes);
            Assert.Equal("Отдел", loaded.Sources.Single().Name);
        }
        finally
        {
            FeatureDataStore.DirectoryOverride = previous;
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}
