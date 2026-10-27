using System.IO;
using System.Text.Json;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Движок ibcmd (выпуск 1.0.0.84): аргументы подключения к файловой и клиент-серверной
/// базе, пользователь ИБ, маскирование паролей, поиск утилиты рядом с платформой,
/// выбор движка в сценарии резервирования и в окне расширений.
/// </summary>
public sealed class IbcmdServiceTests
{
    private static Infobase File(string path) => new()
    {
        Id = "f",
        Name = "Файловая",
        Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = path }
    };

    private static Infobase Server() => new()
    {
        Id = "s",
        Name = "Серверная",
        Connection = new ConnectionSettings { Type = ConnectionType.ClientServer, Server = "srv", DatabaseName = "buh" }
    };

    [Fact]
    public void FileBase_UsesDbPath_WithoutTrailingSeparator()
    {
        var args = IbcmdService.BuildConnectionArguments(File(@"C:\Базы\Бух\"), null, out var error);
        Assert.Null(error);
        Assert.Equal(new[] { @"--db-path=C:\Базы\Бух" }, args);
    }

    [Fact]
    public void ServerBase_WithoutDbmsProfile_ReportsError()
    {
        Assert.Null(IbcmdService.BuildConnectionArguments(Server(), null, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void ServerBase_PostgreSql_ArgumentsAndPort()
    {
        var profile = new DbmsBackupProfile { Dbms = DbmsKind.PostgreSql, Host = "pg", Port = 5433, Database = "buh", User = "postgres", Password = "p w\"d" };
        var args = IbcmdService.BuildConnectionArguments(Server(), profile, out _)!;
        Assert.Equal(new[] { "--dbms=PostgreSQL", "--db-server=pg port=5433", "--db-name=buh", "--db-user=postgres", "--db-pwd=p w\"d" }, args);
    }

    [Fact]
    public void ServerBase_MsSql_PortWithComma_NoPasswordWhenEmpty()
    {
        var profile = new DbmsBackupProfile { Dbms = DbmsKind.MsSql, Host = "sql", Port = 1435, Database = "buh", User = "sa" };
        var args = IbcmdService.BuildConnectionArguments(Server(), profile, out _)!;
        Assert.Contains("--dbms=MSSQLServer", args);
        Assert.Contains("--db-server=sql,1435", args);
        Assert.DoesNotContain(args, a => a.StartsWith("--db-pwd=", StringComparison.Ordinal));
    }

    [Fact]
    public void WebBase_IsRejected()
    {
        var web = new Infobase { Connection = new ConnectionSettings { Type = ConnectionType.WebServer } };
        Assert.Null(IbcmdService.BuildConnectionArguments(web, null, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void UserArguments_OnlyWhenUserSet()
    {
        Assert.Empty(IbcmdService.BuildUserArguments("", "x"));
        Assert.Equal(new[] { "--user=Админ" }, IbcmdService.BuildUserArguments(" Админ ", ""));
        Assert.Equal(new[] { "--user=Админ", "--password=1" }, IbcmdService.BuildUserArguments("Админ", "1"));
    }

    [Fact]
    public void ResolveUser_ScenarioCredentialWins()
    {
        var (user, password) = IbcmdService.ResolveUser(File("/tmp/x"), new BackupCredential { UseInfobaseAuth = false, User = "backup", Password = "b" });
        Assert.Equal("backup", user);
        Assert.Equal("b", password);
    }

    [Fact]
    public void BuildCommand_Order_AndDescribeMasksPasswords()
    {
        var args = IbcmdService.BuildCommand(IbcmdService.DumpCommand,
            new[] { "--db-path=/base", "--db-pwd=secret" }, new[] { "--user=a", "--password=secret" }, new[] { "/tmp/out file.dt" });
        Assert.Equal("infobase", args[0]);
        Assert.Equal("dump", args[1]);
        Assert.Equal("/tmp/out file.dt", args[^1]);
        var text = IbcmdService.DescribeCommand(args);
        Assert.DoesNotContain("secret", text);
        Assert.Contains("--password=***", text);
        Assert.Contains("\"/tmp/out file.dt\"", text);
    }

    [Fact]
    public void CommandWords_MatchIbcmdSyntax()
    {
        Assert.Equal(new[] { "infobase", "restore" }, IbcmdService.RestoreCommand);
        Assert.Equal(new[] { "infobase", "config", "save" }, IbcmdService.ConfigSaveCommand);
        Assert.Equal(new[] { "infobase", "config", "load" }, IbcmdService.ConfigLoadCommand);
        Assert.Equal(new[] { "infobase", "config", "apply" }, IbcmdService.ConfigApplyCommand);
        Assert.Equal("--extension=Доработки", IbcmdService.ExtensionArgument(" Доработки "));
    }

    [Fact]
    public void FindNextTo_FindsSiblingExecutable()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ibcmd_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var platform = Path.Combine(dir, OperatingSystem.IsWindows() ? "1cv8.exe" : "1cv8");
            Assert.Null(IbcmdService.FindNextTo(platform));
            System.IO.File.WriteAllText(Path.Combine(dir, IbcmdService.ExecutableName), "");
            Assert.Equal(Path.Combine(dir, IbcmdService.ExecutableName), IbcmdService.FindNextTo(platform));
            Assert.Null(IbcmdService.FindNextTo(null));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Scenario_DefaultEngine_IsDesigner_AndRoundTripsJson()
    {
        var scenario = new BackupScenario();
        Assert.Equal(BackupEngine.Designer, scenario.Engine);
        scenario.Engine = BackupEngine.Ibcmd;
        var restored = JsonSerializer.Deserialize<BackupScenario>(JsonSerializer.Serialize(scenario))!;
        Assert.Equal(BackupEngine.Ibcmd, restored.Engine);
        var legacy = JsonSerializer.Deserialize<BackupScenario>("{\"Name\":\"old\"}")!;
        Assert.Equal(BackupEngine.Designer, legacy.Engine);
    }

    [Fact]
    public void ScenarioEditViewModel_EngineIndex_MapsAndApplies()
    {
        var vm = new BackupScenarioEditViewModel(new BackupScenario { Name = "s", Engine = BackupEngine.Ibcmd, TargetDirectories = { "/tmp" } });
        Assert.Equal(1, vm.EngineIndex);
        vm.EngineIndex = 0;
        Assert.Equal(BackupEngine.Designer, vm.Engine);
        vm.EngineIndex = 1;
        var target = new BackupScenario();
        vm.ApplyTo(target);
        Assert.Equal(BackupEngine.Ibcmd, target.Engine);
        Assert.Equal(2, BackupScenarioEditViewModel.EngineNames.Count);
    }

    [Fact]
    public void ExtensionsViewModel_UseIbcmd_DefaultOff()
    {
        var vm = new ExtensionsViewModel(new[] { File("/tmp/a") }, null);
        Assert.False(vm.UseIbcmd);
        vm.UseIbcmd = true;
        Assert.True(vm.UseIbcmd);
    }

    [Fact]
    public async Task RunAsync_MissingExecutable_FailsGracefully()
    {
        var result = await IbcmdService.RunAsync(Path.Combine(Path.GetTempPath(), "no_such_ibcmd_" + Guid.NewGuid().ToString("N")),
            new[] { "infobase", "dump" }, Array.Empty<string?>(), TimeSpan.FromSeconds(5));
        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }
}
