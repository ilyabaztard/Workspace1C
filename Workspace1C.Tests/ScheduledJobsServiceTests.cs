using System.Text.Json;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Регламентные задания ИБ (выпуск 1.0.0.85): разбор ответа COM-скрипта, операции
/// скрипта, сводка, исключения массовой операции, логика окна и пункт меню.
/// </summary>
public sealed class ScheduledJobsServiceTests
{
    private static Infobase FileBase(string name) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Name = name,
        Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = "/tmp/" + name }
    };

    [Fact]
    public void ParseJobs_MapsFieldsAndSortsByDisplayName()
    {
        var data = JsonDocument.Parse("""
            {"ok":true,"jobs":[
              {"uuid":"b","name":"ОбменДанными","synonym":"Обмен данными","descr":"","key":"","use":true,"predef":true,"schedule":"Каждый день","state":"Завершено","start":"27.10.2026 10:00:00","error":""},
              {"uuid":"a","name":"Курсы","synonym":"Загрузка курсов","descr":"Курсы валют","key":"k","use":false,"predef":false,"schedule":"","state":"","start":"","error":"Нет сети"}
            ]}
            """).RootElement;
        var jobs = ScheduledJobsService.ParseJobs(data);
        Assert.Equal(new[] { "Курсы валют", "Обмен данными" }, jobs.Select(j => j.DisplayName));
        Assert.False(jobs[0].Use);
        Assert.Equal("Нет сети", jobs[0].LastError);
        Assert.True(jobs[1].Predefined);
        Assert.Equal("Каждый день", jobs[1].Schedule);
    }

    [Fact]
    public void ParseJobs_SingleObject_And_Empty()
    {
        Assert.Single(ScheduledJobsService.ParseJobs(JsonDocument.Parse("""{"jobs":{"uuid":"x","name":"Один"}}""").RootElement));
        Assert.Empty(ScheduledJobsService.ParseJobs(JsonDocument.Parse("""{"jobs":[]}""").RootElement));
        Assert.Empty(ScheduledJobsService.ParseJobs(JsonDocument.Parse("""{}""").RootElement));
    }

    [Fact]
    public void DisplayName_FallsBackToSynonymThenMetadata()
    {
        Assert.Equal("Синоним", new InfobaseScheduledJob { MetadataName = "М", MetadataSynonym = "Синоним" }.DisplayName);
        Assert.Equal("М", new InfobaseScheduledJob { MetadataName = "М" }.DisplayName);
    }

    [Fact]
    public void Script_ContainsOperations_AndFits()
    {
        var script = ComScriptRunner.BuildScript(ScheduledJobsService.OperationBody);
        foreach (var op in new[] { "'list'", "'setuse'", "'setall'", "GetScheduledJobs", "FindByUUID" })
            Assert.Contains(op, script);
        Assert.True(ComScriptRunner.EncodeCommand(script).Length < ComScriptRunner.MaxEncodedCommandLength);
    }

    [Fact]
    public void GetInt_ReadsNumbersOnly()
    {
        var data = JsonDocument.Parse("""{"changed":5,"total":"x"}""").RootElement;
        Assert.Equal(5, ScheduledJobsService.GetInt(data, "changed"));
        Assert.Equal(0, ScheduledJobsService.GetInt(data, "total"));
        Assert.Equal(0, ScheduledJobsService.GetInt(data, "missing"));
    }

    [Fact]
    public void BuildSummary_CountsEnabledAndErrors()
    {
        var text = ScheduledJobsViewModel.BuildSummary(new[]
        {
            new InfobaseScheduledJob { Use = true },
            new InfobaseScheduledJob { Use = true, LastError = "e" },
            new InfobaseScheduledJob()
        });
        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    [Fact]
    public void AddSelectedToKeep_AppendsOnce()
    {
        var vm = new ScheduledJobsViewModel(new[] { FileBase("A") }, null) { KeepText = "Курсы" };
        vm.SelectedJob = new ScheduledJobRow(new InfobaseScheduledJob { MetadataName = "Обмен" });
        vm.AddSelectedToKeep();
        vm.AddSelectedToKeep();
        Assert.Equal("Курсы, Обмен", vm.KeepText);
    }

    [Fact]
    public void ViewModel_PreselectsBase_FiltersWeb()
    {
        var a = FileBase("Альфа");
        var web = new Infobase { Name = "Веб", Connection = new ConnectionSettings { Type = ConnectionType.WebServer } };
        var vm = new ScheduledJobsViewModel(new[] { web, a }, a);
        Assert.Single(vm.Targets);
        Assert.Same(a, vm.SelectedTarget!.Infobase);
        Assert.Single(vm.CheckedTargets);
    }

    [Fact]
    public async Task SetAll_WithoutChecked_ReturnsZero()
    {
        var vm = new ScheduledJobsViewModel(new[] { FileBase("A") }, null);
        Assert.Equal(0, await vm.SetAllInCheckedAsync(false));
        Assert.False(string.IsNullOrEmpty(vm.LogText));
    }

    [Fact]
    public void Row_LastRun_CombinesStartAndState()
    {
        Assert.Equal("10:00 · ok", new ScheduledJobRow(new InfobaseScheduledJob { LastStart = "10:00", LastState = "ok" }).LastRun);
        Assert.Equal("ok", new ScheduledJobRow(new InfobaseScheduledJob { LastState = "ok" }).LastRun);
        Assert.Equal("10:00", new ScheduledJobRow(new InfobaseScheduledJob { LastStart = "10:00" }).LastRun);
    }

    [Fact]
    public void MenuCatalog_HasScheduledJobsItem()
    {
        var item = UtilitiesMenuCatalog.All.Single(i => i.Id == "InfobaseScheduledJobs");
        Assert.Equal(UtilitiesMenuCommandKind.InfobaseScheduledJobs, item.CommandKind);
    }

    [Fact]
    public async Task RunAsync_RealPowerShell_UnknownProgId_ReportsNoProgId()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var result = await ComScriptRunner.RunAsync(ScheduledJobsService.OperationBody,
            new[] { "Workspace1C.Nonexistent.ProgId" }, "File=\"C:\\nonexistent\";",
            new Dictionary<string, object?> { ["op"] = "list" }, null, TimeSpan.FromSeconds(90));
        Assert.Equal("noprogid", result.Code);
    }
}
