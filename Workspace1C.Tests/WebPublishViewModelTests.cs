using System.Collections.Generic;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #63 (часть B): выбор обнаруженного инстанса Apache в окне публикации —
/// автозаполнение конфигурации/каталога и выбор конфиг-файла для webinst
/// (основной conf или include-файл).
/// </summary>
public sealed class WebPublishViewModelTests
{
    private static Infobase Infobase() => new()
    {
        Id = "test-base-id",
        Name = "Бухгалтерия предприятия"
    };

    private static ApacheInstance Apache24WithInclude() => new()
    {
        Id = "Apache2.4",
        Name = "Apache2.4",
        Description = "Apache HTTP Server",
        ServerRoot = @"C:\Apache24",
        ConfPath = @"C:\Apache24\conf\httpd.conf",
        Htdocs = @"C:\Apache24\htdocs",
        Kind = WebServerKind.Apache24,
        IncludeFiles = new[] { @"C:\Apache24\conf\extra\httpd-vhosts.conf" },
        IncludeDirs = new[] { @"C:\Apache24\conf\extra\sites\*.conf" }
    };

    [Fact]
    public void WebPublishViewModel_SelectingApacheInstance_SetsConfAndPhysicalFromInstance()
    {
        var ib = Infobase();
        var instance = Apache24WithInclude();
        var vm = new WebPublishViewModel(new[] { ib }, ib, new[] { instance });

        var optionIndex = vm.ServerIndexFor(instance);
        Assert.True(optionIndex > 0, "Пункт инстанса не найден в ServerOptions");
        vm.ServerIndex = optionIndex;
        vm.VirtualDirectory = "test_trade";

        Assert.True(vm.IsApache);
        Assert.Same(instance, vm.SelectedApacheInstance);
        Assert.Equal(@"C:\Apache24\conf\httpd.conf", vm.ApacheConfPath);
        Assert.Equal(@"C:\Apache24\htdocs\test_trade", vm.PhysicalDirectory);
        Assert.True(vm.HasConfTargets);
        Assert.Equal(2, vm.ConfTargets.Count);
        Assert.Equal(0, vm.ConfTargetIndex);
    }

    [Fact]
    public void WebPublishViewModel_SelectingIncludeConfTarget_SetsApacheConfPathToIncludeFile()
    {
        var ib = Infobase();
        var instance = Apache24WithInclude();
        var vm = new WebPublishViewModel(new[] { ib }, ib, new[] { instance });

        vm.ServerIndex = vm.ServerIndexFor(instance);
        // Второй вариант — include-файл из конфига инстанса.
        vm.ConfTargetIndex = 1;

        Assert.Equal(@"C:\Apache24\conf\extra\httpd-vhosts.conf", vm.ApacheConfPath);
        Assert.Equal(@"C:\Apache24\conf\extra\httpd-vhosts.conf", vm.BuildRequest().ApacheConfPath);
        Assert.Equal(WebServerKind.Apache24, vm.BuildRequest().Server);
    }

    /// <summary>webinst допустимо указывать include-файл: Validate требует лишь непустой путь.</summary>
    [Fact]
    public void WebPublishService_Validate_ConfPathIsIncludeFile_Allowed()
    {
        var request = new WebPublishRequest
        {
            Server = WebServerKind.Apache24,
            VirtualDirectory = "trade",
            PhysicalDirectory = @"C:\Apache24\htdocs\trade",
            ApacheConfPath = @"C:\Apache24\conf\extra\httpd-vhosts.conf",
            ConnectionString = "Srvr=\"srv\";Ref=\"trade\";"
        };
        Assert.Null(WebPublishService.Validate(request));
        Assert.Equal(new[] { "-publish", "-apache24", "-wsdir", "trade", "-dir", @"C:\Apache24\htdocs\trade",
            "-connstr", "Srvr=\"srv\";Ref=\"trade\";", "-confpath", @"C:\Apache24\conf\extra\httpd-vhosts.conf" },
            WebPublishService.BuildPublishArguments(request));
    }

    [Fact]
    public void WebPublishViewModel_ConfTargetIndex_OutOfRange_ClampedToExisting()
    {
        var ib = Infobase();
        var instance = Apache24WithInclude();
        var vm = new WebPublishViewModel(new[] { ib }, ib, new[] { instance });
        vm.ServerIndex = vm.ServerIndexFor(instance);
        vm.ConfTargetIndex = 99;
        Assert.InRange(vm.ConfTargetIndex, 0, vm.ConfTargets.Count - 1);
        Assert.Equal(vm.ConfTargets.Count - 1, vm.ConfTargetIndex);
    }
}