using System.Collections.Generic;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты вью-модели окна «Сменить платформу» (issue #54, переработка по комментарию
/// 7OH): отдельное окно из блока «Для выделенных (N)…» — выбор установленной версии
/// платформы 1С и замена <see cref="Infobase.PlatformVersion"/> кандидатов по «Сменить».
/// Применение через <see cref="ConnectionReplacementPlanner.ApplyPlatform"/>, колбэк —
/// тот же, что у замены строк подключения (undo-история и персистентность).
/// </summary>
public sealed class PlatformChangeViewModelTests
{
    private static Infobase Base(string id, string name, string platform, string server = "server1")
        => new()
        {
            Id = id,
            Name = name,
            PlatformVersion = platform,
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.ClientServer,
                Server = server,
                DatabaseName = "База",
                Port = 1541
            }
        };

    private static PlatformChangeViewModel CreateVm(
        IReadOnlyList<Infobase> candidates,
        IReadOnlyList<string>? installedPlatforms = null,
        Action<IReadOnlyList<ConnectionReplaceUndoEntry>>? onApplied = null)
        => new(candidates, installedPlatforms, onApplied);

    [Fact]
    public void Apply_ChangesPlatformVersion_ForAllCandidates_AndInvokesCallback()
    {
        var candidates = new List<Infobase>
        {
            Base("1", "База А", "8.3.22.1483"),
            Base("2", "База Б", "8.3.24.1664")
        };
        IReadOnlyList<ConnectionReplaceUndoEntry>? applied = null;
        var vm = CreateVm(candidates, new[] { "8.3.27.1644" }, entries => applied = entries);

        vm.ApplyCommand.Execute(null);

        // Платформа заменена у всех кандидатов, строка подключения не тронута.
        Assert.All(candidates, c => Assert.Equal("8.3.27.1644", c.PlatformVersion));
        Assert.All(candidates, c => Assert.Equal("server1", c.Connection.Server));
        Assert.NotNull(applied);
        Assert.Equal(2, applied!.Count);
        Assert.All(applied, e => Assert.Equal("8.3.27.1644", e.AfterPlatform));
        Assert.Contains(applied, e => e.BeforePlatform is "8.3.22.1483");
        Assert.Contains(applied, e => e.BeforePlatform is "8.3.24.1664");
        Assert.Equal(string.Format(
            LocalizationManager.T("PlatformChange.AppliedFormat"), 2), vm.ResultText);
    }

    [Fact]
    public void Apply_SkipsCandidates_WithSameVersion()
    {
        var candidates = new List<Infobase>
        {
            Base("1", "База А", "8.3.27.1644"),
            Base("2", "База Б", "8.3.22.1483")
        };
        IReadOnlyList<ConnectionReplaceUndoEntry>? applied = null;
        var vm = CreateVm(candidates, new[] { "8.3.27.1644" }, entries => applied = entries);

        vm.ApplyCommand.Execute(null);

        // База с уже совпадающей версией не попадает в записи отката.
        Assert.Equal("8.3.27.1644", candidates[1].PlatformVersion);
        Assert.NotNull(applied);
        var entry = Assert.Single(applied!);
        Assert.Equal("2", entry.Infobase.Id);
    }

    [Fact]
    public void CannotApply_AndApply_NoOp_WhenNoInstalledPlatforms()
    {
        var candidates = new List<Infobase> { Base("1", "База", "8.3.22.1483") };
        IReadOnlyList<ConnectionReplaceUndoEntry>? applied = null;
        var vm = CreateVm(candidates, installedPlatforms: null, entries => applied = entries);

        Assert.False(vm.CanApply);
        Assert.Empty(vm.InstalledPlatformVersions);
        Assert.Equal(LocalizationManager.T("PlatformChange.EmptyList"), vm.SummaryText);

        vm.ApplyCommand.Execute(null);

        // No-op: ни платформа, ни колбэк не пострадали.
        Assert.Equal("8.3.22.1483", candidates[0].PlatformVersion);
        Assert.Null(applied);
        Assert.Equal(string.Empty, vm.ResultText);
    }

    [Fact]
    public void CannotApply_AndApply_NoOp_WhenNoCandidates()
    {
        var vm = CreateVm(new List<Infobase>(), new[] { "8.3.27.1644" });

        Assert.False(vm.CanApply);
        Assert.Equal(string.Format(
            LocalizationManager.T("PlatformChange.SummaryFormat"), 0), vm.SummaryText);

        vm.ApplyCommand.Execute(null);

        Assert.Equal(string.Empty, vm.ResultText);
    }

    [Fact]
    public void SelectedPlatform_PrefillsFirstInstalledVersion()
    {
        var vm = CreateVm(
            new List<Infobase> { Base("1", "База", "8.3.22.1483") },
            new[] { "8.3.24.1664", "8.3.22.1483" });

        Assert.Equal("8.3.24.1664", vm.SelectedPlatform);
        Assert.True(vm.CanApply);
        Assert.Equal(string.Format(
            LocalizationManager.T("PlatformChange.SummaryFormat"), 1), vm.SummaryText);
    }
}