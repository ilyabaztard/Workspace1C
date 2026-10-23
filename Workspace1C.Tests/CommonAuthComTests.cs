#if !LINUX
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #57 «добавить общую авторизацию»: общая авторизация используется
/// и при COM-чтении сведений о конфигурации (<see cref="OneCComConnector.
/// BuildComConnectString"/>): для ИБ без собственной авторизации в строку
/// подключения попадают Usr/Pwd из общей. Заданная собственной не перекрывается.
/// Файл обёрнут в #if !LINUX: класс недоступен в Linux-сборке.
/// Статик <see cref="OneCLauncher.CommonAuthorization"/> сбрасывается в finally.
/// </summary>
[Collection("CommonAuthStatics")]
public sealed class CommonAuthComTests
{
    private static Infobase FileBase(AuthenticationMode mode, string user = "", string password = "")
    {
        var ib = new Infobase { Name = "Тест" };
        ib.Connection.Type = ConnectionType.File;
        ib.Connection.FilePath = "/tmp/base";
        ib.Connection.AuthenticationMode = mode;
        ib.Connection.User = user;
        ib.Connection.Password = password;
        return ib;
    }

    [Fact]
    public void DefaultBase_WithCommonAuth_AddsUsrPwd()
    {
        var ib = FileBase(AuthenticationMode.Prompt);
        try
        {
            OneCLauncher.CommonAuthorization = new InfobaseAuthSettings
            {
                AuthenticationMode = AuthenticationMode.Credentials,
                User = "comU",
                Password = "comP"
            };

            var result = OneCComConnector.BuildComConnectString(
                ib, OneCLaunchMode.Configurator, out _);

            Assert.Contains("Usr=\"comU\"", result);
            Assert.Contains("Pwd=\"comP\"", result);
        }
        finally
        {
            OneCLauncher.CommonAuthorization = null;
        }
    }

    [Fact]
    public void OwnConnection_OverridesCommonAuth()
    {
        var ib = FileBase(AuthenticationMode.Credentials, "ownU", "ownP");
        try
        {
            OneCLauncher.CommonAuthorization = new InfobaseAuthSettings
            {
                AuthenticationMode = AuthenticationMode.Credentials,
                User = "comU",
                Password = "comP"
            };

            var result = OneCComConnector.BuildComConnectString(
                ib, OneCLaunchMode.Configurator, out _);

            Assert.Contains("Usr=\"ownU\"", result);
            Assert.Contains("Pwd=\"ownP\"", result);
            Assert.DoesNotContain("comU", result);
            Assert.DoesNotContain("comP", result);
        }
        finally
        {
            OneCLauncher.CommonAuthorization = null;
        }
    }

    // ---- Раздельная общая авторизация по режимам запуска (issue #57) ----

    [Fact]
    public void ConfiguratorCommonAuth_AddsUsrPwd()
    {
        // Чтение сведений идёт в режиме «Конфигуратор» — применяется раздельная
        // общая для Конфигуратора.
        var ib = FileBase(AuthenticationMode.Prompt);
        try
        {
            OneCLauncher.CommonConfiguratorAuthorization = new InfobaseAuthSettings
            {
                AuthenticationMode = AuthenticationMode.Credentials,
                User = "cfgComU",
                Password = "cfgComP"
            };

            var result = OneCComConnector.BuildComConnectString(
                ib, OneCLaunchMode.Configurator, out var hasSecret);

            Assert.Contains("Usr=\"cfgComU\"", result);
            Assert.Contains("Pwd=\"cfgComP\"", result);
            Assert.True(hasSecret);
        }
        finally
        {
            OneCLauncher.CommonAuthorization = null;
            OneCLauncher.CommonEnterpriseAuthorization = null;
            OneCLauncher.CommonConfiguratorAuthorization = null;
        }
    }

    [Fact]
    public void EnterpriseCommonAuth_NotUsedForConfiguratorCom()
    {
        // Для чтения сведений (режим Configurator) раздельная для «1С:Предприятия»
        // не применяется: без legacy общих учётных данных строка остаётся без Usr/Pwd.
        var ib = FileBase(AuthenticationMode.Prompt);
        try
        {
            OneCLauncher.CommonEnterpriseAuthorization = new InfobaseAuthSettings
            {
                AuthenticationMode = AuthenticationMode.Credentials,
                User = "entComU",
                Password = "entComP"
            };

            var result = OneCComConnector.BuildComConnectString(
                ib, OneCLaunchMode.Configurator, out _);

            Assert.DoesNotContain("entComU", result);
        }
        finally
        {
            OneCLauncher.CommonAuthorization = null;
            OneCLauncher.CommonEnterpriseAuthorization = null;
            OneCLauncher.CommonConfiguratorAuthorization = null;
        }
    }

    [Fact]
    public void ComUsesEnterpriseCommonAuth_ForEnterpriseMode()
    {
        // Строка подключения строится по переданному режиму: для Enterprise-режима
        // применяется раздельная общая для «1С:Предприятия».
        var ib = FileBase(AuthenticationMode.Prompt);
        try
        {
            OneCLauncher.CommonEnterpriseAuthorization = new InfobaseAuthSettings
            {
                AuthenticationMode = AuthenticationMode.Credentials,
                User = "entComU",
                Password = "entComP"
            };

            var result = OneCComConnector.BuildComConnectString(
                ib, OneCLaunchMode.Enterprise, out _);

            Assert.Contains("Usr=\"entComU\"", result);
            Assert.Contains("Pwd=\"entComP\"", result);
        }
        finally
        {
            OneCLauncher.CommonAuthorization = null;
            OneCLauncher.CommonEnterpriseAuthorization = null;
            OneCLauncher.CommonConfiguratorAuthorization = null;
        }
    }
}
#endif