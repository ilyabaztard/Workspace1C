using System;
using System.IO;
using Workspace1C.Models;
using Workspace1C.Services.EventLog;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты ViewModel окна «Журнал регистрации» (issue #74): автоподстановка базы
/// при открытии из Утилиты и смена базы селектором вверху формы.
/// </summary>
public sealed class EventLogViewModelTests
{
    [Fact]
    public void Constructor_SelectedFileBase_ResolvesLocationAndTitle()
    {
        using var tmp = TempBase.Create();
        var ib = FileBase(tmp.BaseDir, "Бухгалтерия");

        var vm = new EventLogViewModel(new[] { ib }, ib);

        Assert.Equal(0, vm.SelectedIndex);
        Assert.Same(ib, vm.SelectedInfobase);
        Assert.Equal("Бухгалтерия", vm.Title);
        Assert.True(vm.Location.IsValid);
        Assert.Equal(Path.Combine(tmp.BaseDir, "1Cv8Log"), vm.Location.LogDir);
        Assert.Equal(new[] { "Бухгалтерия" }, vm.InfobaseNames);
    }

    [Fact]
    public void Constructor_NoSelectedBase_SelectsFirst()
    {
        using var tmp1 = TempBase.Create();
        using var tmp2 = TempBase.Create();
        var first = FileBase(tmp1.BaseDir, "Альфа");
        var second = FileBase(tmp2.BaseDir, "Бета");

        var vm = new EventLogViewModel(new[] { first, second }, null);

        Assert.Equal(0, vm.SelectedIndex);
        Assert.Same(first, vm.SelectedInfobase);
    }

    [Fact]
    public void Constructor_NoBases_NotValidLocation()
    {
        var vm = new EventLogViewModel(Array.Empty<Infobase>(), null);

        Assert.Equal(-1, vm.SelectedIndex);
        Assert.Null(vm.SelectedInfobase);
        Assert.False(vm.Location.IsValid);
    }

    [Fact]
    public void ChangeSelectedIndex_UpdatesLocationAndTitle()
    {
        using var tmp1 = TempBase.Create();
        using var tmp2 = TempBase.Create();
        var first = FileBase(tmp1.BaseDir, "Альфа");
        var second = FileBase(tmp2.BaseDir, "Бета");

        var vm = new EventLogViewModel(new[] { first, second }, first);
        Assert.Equal(Path.Combine(tmp1.BaseDir, "1Cv8Log"), vm.Location.LogDir);

        vm.SelectedIndex = 1;

        Assert.Same(second, vm.SelectedInfobase);
        Assert.Equal(Path.Combine(tmp2.BaseDir, "1Cv8Log"), vm.Location.LogDir);
        Assert.Equal("Бета", vm.Title);
    }

    [Fact]
    public void ClientServerBase_LocationNotValid()
    {
        var ib = new Infobase
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Серверная база",
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.ClientServer,
                Server = "srv-1c",
                DatabaseName = "AccountingCorp"
            }
        };

        var vm = new EventLogViewModel(new[] { ib }, ib);

        Assert.False(vm.Location.IsValid);
        Assert.NotNull(vm.Location.ErrorMessage);
        Assert.Contains("сервере", vm.Location.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InfobaseNames_SortedByName()
    {
        using var tmp1 = TempBase.Create();
        using var tmp2 = TempBase.Create();
        var zeta = FileBase(tmp1.BaseDir, "Зета");
        var alpha = FileBase(tmp2.BaseDir, "Альфа");

        var vm = new EventLogViewModel(new[] { zeta, alpha }, null);

        Assert.Equal(new[] { "Альфа", "Зета" }, vm.InfobaseNames);
    }

    [Fact]
    public void ChangeLocation_ManualFolder_StillWorks()
    {
        using var tmp = TempBase.Create();
        var ib = FileBase(tmp.BaseDir, "База");
        var vm = new EventLogViewModel(new[] { ib }, ib);

        var manual = LgdJournalLocator.FromDirectory(Path.Combine(tmp.BaseDir, "1Cv8Log"));
        vm.ChangeLocation(manual);

        Assert.True(vm.Location.IsValid);
        Assert.Equal(Path.Combine(tmp.BaseDir, "1Cv8Log"), vm.Location.LogDir);
    }

    private static Infobase FileBase(string baseDir, string name) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Name = name,
        Connection = new ConnectionSettings
        {
            Type = ConnectionType.File,
            FilePath = Path.Combine(baseDir, "1Cv8.1CD")
        }
    };

    /// <summary>Temp-каталог «файловой базы» с журналом 1Cv8Log, удаляется при Dispose.</summary>
    private sealed class TempBase : IDisposable
    {
        public string BaseDir { get; }

        private TempBase(string dir) => BaseDir = dir;

        public static TempBase Create()
        {
            var dir = Path.Combine(Path.GetTempPath(), $"evlog-vm-{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            var logDir = Path.Combine(dir, "1Cv8Log");
            Directory.CreateDirectory(logDir);
            File.WriteAllBytes(Path.Combine(logDir, "1Cv8.lgd"), Array.Empty<byte>());
            return new TempBase(dir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(BaseDir))
                    Directory.Delete(BaseDir, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}