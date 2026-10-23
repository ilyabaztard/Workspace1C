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
}
#endif