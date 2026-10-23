using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #57 «добавить общую авторизацию»: единый выбор учётных данных
/// (<see cref="InfobaseAuthResolver.Resolve"/>) с fallback на общую авторизацию
/// при незаданной собственной (для режима или базы). Приоритет:
/// EnterpriseAuth/ConfiguratorAuth → Connection → общая авторизация → «Запрос».
/// </summary>
[Collection("CommonAuthStatics")]
public sealed class CommonAuthResolverTests
{
    private static InfobaseAuthSettings Common(string user, string password,
        AuthenticationMode mode = AuthenticationMode.Credentials)
        => new() { AuthenticationMode = mode, User = user, Password = password };

    private static Infobase FileBase(AuthenticationMode mode, string user, string password)
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
    public void EnterpriseAuth_OverridesCommonAuth()
    {
        var ib = FileBase(AuthenticationMode.Prompt, "", "");
        ib.EnterpriseAuth = Common("entU", "entP");

        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Enterprise,
            out var mode, out var user, out var pass, Common("comU", "comP"));

        Assert.Equal(AuthenticationMode.Credentials, mode);
        Assert.Equal("entU", user);
        Assert.Equal("entP", pass);
    }

    [Fact]
    public void ConfiguratorAuth_OverridesCommonAuth()
    {
        var ib = FileBase(AuthenticationMode.Prompt, "", "");
        ib.ConfiguratorAuth = Common("cfgU", "cfgP");

        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Configurator,
            out var mode, out var user, out var pass, Common("comU", "comP"));

        Assert.Equal(AuthenticationMode.Credentials, mode);
        Assert.Equal("cfgU", user);
        Assert.Equal("cfgP", pass);
    }

    [Fact]
    public void Connection_OverridesCommonAuth()
    {
        var ib = FileBase(AuthenticationMode.Credentials, "connU", "connP");

        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Enterprise,
            out var mode, out var user, out var pass, Common("comU", "comP"));

        Assert.Equal("connU", user);
        Assert.Equal("connP", pass);
    }

    [Fact]
    public void NoOwnAuth_FallsBackToCommonAuth()
    {
        // Дефолтная Connection (Prompt, пустые реквизиты) и нет раздельной авторизации.
        var ib = FileBase(AuthenticationMode.Prompt, "", "");

        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Enterprise,
            out var mode, out var user, out var pass, Common("comU", "comP"));

        Assert.Equal(AuthenticationMode.Credentials, mode);
        Assert.Equal("comU", user);
        Assert.Equal("comP", pass);
    }

    [Fact]
    public void DefaultCommonAuth_IsIgnored()
    {
        // Общая авторизация со значениями по умолчанию (IsDefault) считается незаданной.
        var ib = FileBase(AuthenticationMode.Prompt, "", "");

        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Enterprise,
            out var mode, out var user, out var pass, new InfobaseAuthSettings());

        Assert.Equal(AuthenticationMode.Prompt, mode);
        Assert.Equal(string.Empty, user);
        Assert.Equal(string.Empty, pass);
    }

    [Fact]
    public void NothingSet_DefaultsToPrompt()
    {
        var ib = FileBase(AuthenticationMode.Prompt, "", "");

        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Enterprise,
            out var mode, out var user, out var pass);

        Assert.Equal(AuthenticationMode.Prompt, mode);
        Assert.Equal(string.Empty, user);
        Assert.Equal(string.Empty, pass);
    }

    [Fact]
    public void OldSignature_WithoutCommonAuth_Works()
    {
        // Обратная совместимость: вызовы без параметра общей авторизации работают.
        var ib = FileBase(AuthenticationMode.Credentials, "connU", "connP");

        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Enterprise,
            out var mode, out var user, out var pass);

        Assert.Equal("connU", user);
        Assert.Equal("connP", pass);
    }

    [Fact]
    public void DefaultEnterpriseAuth_FallsBackToConnection()
    {
        // НЮАНС (issue #57): «дефолтный» EnterpriseAuth (Prompt/пусто) теперь считается
        // незаданным и даёт fallback на Connection, затем на общую авторизацию.
        var ib = FileBase(AuthenticationMode.Credentials, "connU", "connP");
        ib.EnterpriseAuth = new InfobaseAuthSettings(); // IsDefault == true

        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Enterprise,
            out var mode, out var user, out var pass, Common("comU", "comP"));

        Assert.Equal("connU", user);
        Assert.Equal("connP", pass);
    }

    [Fact]
    public void DefaultEnterpriseAuth_FallsBackToCommonAuth()
    {
        // Дефолтный EnterpriseAuth + дефолтная Connection → общая авторизация.
        var ib = FileBase(AuthenticationMode.Prompt, "", "");
        ib.EnterpriseAuth = new InfobaseAuthSettings(); // IsDefault == true

        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Enterprise,
            out var mode, out var user, out var pass, Common("comU", "comP"));

        Assert.Equal("comU", user);
        Assert.Equal("comP", pass);
    }

    [Fact]
    public void CommonAuth_WindowsMode_ResolvesOsAuthentication()
    {
        var ib = FileBase(AuthenticationMode.Prompt, "", "");
        var common = Common("", "", AuthenticationMode.Windows);

        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Enterprise,
            out var mode, out var user, out var pass, common);

        Assert.Equal(AuthenticationMode.Windows, mode);
    }
}