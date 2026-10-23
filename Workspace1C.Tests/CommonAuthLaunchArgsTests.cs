using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #57 «добавить общую авторизацию»: общая авторизация подставляется при
/// запуске 1С (<see cref="OneCLauncher.BuildArguments"/>) для ИБ без собственной
/// авторизации и не перекрывает заданную. «Запуск с аутентификацией» (issue #48)
/// по-прежнему подавляет любые учётные данные ключом /WA-.
/// Статик <see cref="OneCLauncher.CommonAuthorization"/> всегда сбрасывается
/// в finally, чтобы не задеть параллельные тесты.
/// </summary>
[Collection("CommonAuthStatics")]
public sealed class CommonAuthLaunchArgsTests
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
    public void DefaultBase_WithCommonAuth_AddsCredentials()
    {
        var ib = FileBase(AuthenticationMode.Prompt);
        try
        {
            OneCLauncher.CommonAuthorization = new InfobaseAuthSettings
            {
                AuthenticationMode = AuthenticationMode.Credentials,
                User = "common",
                Password = "secret"
            };

            var args = OneCLauncher.BuildArguments(ib, OneCLaunchMode.Enterprise, null, null);

            Assert.Contains("/N\"common\"", args);
            Assert.Contains("/P\"secret\"", args);
        }
        finally
        {
            OneCLauncher.CommonAuthorization = null;
        }
    }

    [Fact]
    public void OwnConnection_NotOverriddenByCommonAuth()
    {
        var ib = FileBase(AuthenticationMode.Credentials, "own", "ownPass");
        try
        {
            OneCLauncher.CommonAuthorization = new InfobaseAuthSettings
            {
                AuthenticationMode = AuthenticationMode.Credentials,
                User = "common",
                Password = "secret"
            };

            var args = OneCLauncher.BuildArguments(ib, OneCLaunchMode.Enterprise, null, null);

            Assert.Contains("/N\"own\"", args);
            Assert.Contains("/P\"ownPass\"", args);
            Assert.DoesNotContain("common", args);
            Assert.DoesNotContain("secret", args);
        }
        finally
        {
            OneCLauncher.CommonAuthorization = null;
        }
    }

    [Fact]
    public void RequestAuthPrompt_SuppressesCommonAuth()
    {
        var ib = FileBase(AuthenticationMode.Prompt);
        try
        {
            OneCLauncher.CommonAuthorization = new InfobaseAuthSettings
            {
                AuthenticationMode = AuthenticationMode.Credentials,
                User = "common",
                Password = "secret"
            };

            OneCLauncher.RequestAuthPrompt(ib);
            var args = OneCLauncher.BuildArguments(ib, OneCLaunchMode.Enterprise, null, null);

            // «Запуск с аутентификацией» (issue #48): общая авторизация не подставляется.
            Assert.DoesNotContain("/N\"", args);
            Assert.DoesNotContain("/P\"", args);
            Assert.Contains(" /WA-", args);
        }
        finally
        {
            OneCLauncher.CommonAuthorization = null;
        }
    }

    [Fact]
    public void CommonAuth_WindowsMode_AddsWaPlus()
    {
        var ib = FileBase(AuthenticationMode.Prompt);
        try
        {
            OneCLauncher.CommonAuthorization = new InfobaseAuthSettings
            {
                AuthenticationMode = AuthenticationMode.Windows
            };

            var args = OneCLauncher.BuildArguments(ib, OneCLaunchMode.Enterprise, null, null);

            Assert.Contains(" /WA+", args);
            Assert.DoesNotContain("/N\"", args);
            Assert.DoesNotContain("/P\"", args);
        }
        finally
        {
            OneCLauncher.CommonAuthorization = null;
        }
    }

    [Fact]
    public void ConfiguratorMode_WithCommonAuth_AddsCredentials()
    {
        var ib = FileBase(AuthenticationMode.Prompt);
        try
        {
            OneCLauncher.CommonAuthorization = new InfobaseAuthSettings
            {
                AuthenticationMode = AuthenticationMode.Credentials,
                User = "common",
                Password = "secret"
            };

            var args = OneCLauncher.BuildArguments(ib, OneCLaunchMode.Configurator, null, null);

            Assert.Contains("/N\"common\"", args);
            Assert.Contains("/P\"secret\"", args);
        }
        finally
        {
            OneCLauncher.CommonAuthorization = null;
        }
    }
}