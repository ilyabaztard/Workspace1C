using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #57 «добавить общую авторизацию»: единый выбор учётных данных
/// (<see cref="InfobaseAuthResolver.Resolve"/>) с fallback на общую авторизацию
/// при незаданной собственной (для режима или базы). Приоритет:
/// EnterpriseAuth/ConfiguratorAuth → Connection → общая авторизация по режиму
/// (раздельная Enterprise/Configurator, при незаданной — legacy) → «Запрос».
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

    // ---- Раздельная общая авторизация по режимам запуска (issue #57) ----

    [Fact]
    public void LegacyCommonAuth_UsedForBothModes_WhenSplitNotSet()
    {
        // Регрессия: раздельные общие авторизации не заданы — legacy применяется
        // и для «1С:Предприятия», и для «Конфигуратора».
        var ib = FileBase(AuthenticationMode.Prompt, "", "");
        var legacy = Common("legacyU", "legacyP");

        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Enterprise,
            out var entMode, out var entUser, out var entPass, null, null, legacy);
        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Configurator,
            out var cfgMode, out var cfgUser, out var cfgPass, null, null, legacy);

        Assert.Equal(AuthenticationMode.Credentials, entMode);
        Assert.Equal("legacyU", entUser);
        Assert.Equal("legacyP", entPass);
        Assert.Equal(AuthenticationMode.Credentials, cfgMode);
        Assert.Equal("legacyU", cfgUser);
        Assert.Equal("legacyP", cfgPass);
    }

    [Fact]
    public void CommonEnterpriseAuth_UsedForEnterprise_FallsBackForConfigurator()
    {
        // Раздельная общая задана только для «1С:Предприятия»: в Enterprise-режиме
        // применяется она, в Configurator — legacy.
        var ib = FileBase(AuthenticationMode.Prompt, "", "");
        var enterprise = Common("entU", "entP");
        var legacy = Common("legacyU", "legacyP");

        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Enterprise,
            out var entMode, out var entUser, out var entPass, enterprise, null, legacy);
        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Configurator,
            out var cfgMode, out var cfgUser, out var cfgPass, enterprise, null, legacy);

        Assert.Equal(AuthenticationMode.Credentials, entMode);
        Assert.Equal("entU", entUser);
        Assert.Equal("entP", entPass);
        Assert.Equal(AuthenticationMode.Credentials, cfgMode);
        Assert.Equal("legacyU", cfgUser);
        Assert.Equal("legacyP", cfgPass);
    }

    [Fact]
    public void CommonConfiguratorAuth_UsedForConfigurator_FallsBackForEnterprise()
    {
        // Раздельная общая задана только для «Конфигуратора»: в Configurator-режиме
        // применяется она, в Enterprise — legacy.
        var ib = FileBase(AuthenticationMode.Prompt, "", "");
        var configurator = Common("cfgU", "cfgP");
        var legacy = Common("legacyU", "legacyP");

        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Enterprise,
            out var entMode, out var entUser, out var entPass, null, configurator, legacy);
        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Configurator,
            out var cfgMode, out var cfgUser, out var cfgPass, null, configurator, legacy);

        Assert.Equal(AuthenticationMode.Credentials, entMode);
        Assert.Equal("legacyU", entUser);
        Assert.Equal("legacyP", entPass);
        Assert.Equal(AuthenticationMode.Credentials, cfgMode);
        Assert.Equal("cfgU", cfgUser);
        Assert.Equal("cfgP", cfgPass);
    }

    [Fact]
    public void DefaultCommonEnterpriseAuth_FallsBackToLegacy()
    {
        // «Дефолтная» (IsDefault — «Запрос» с пустыми реквизитами) раздельная общая
        // для «1С:Предприятия» считается незаданной — даёт fallback на legacy.
        var ib = FileBase(AuthenticationMode.Prompt, "", "");
        var legacy = Common("legacyU", "legacyP");

        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Enterprise,
            out var mode, out var user, out var pass, new InfobaseAuthSettings(), null, legacy);

        Assert.Equal(AuthenticationMode.Credentials, mode);
        Assert.Equal("legacyU", user);
        Assert.Equal("legacyP", pass);
    }

    [Fact]
    public void DefaultCommonConfiguratorAuth_FallsBackToLegacy()
    {
        // «Дефолтная» раздельная общая для «Конфигуратора» считается незаданной —
        // даёт fallback на legacy.
        var ib = FileBase(AuthenticationMode.Prompt, "", "");
        var legacy = Common("legacyU", "legacyP");

        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Configurator,
            out var mode, out var user, out var pass, null, new InfobaseAuthSettings(), legacy);

        Assert.Equal(AuthenticationMode.Credentials, mode);
        Assert.Equal("legacyU", user);
        Assert.Equal("legacyP", pass);
    }

    [Fact]
    public void CommonEnterpriseAuth_WindowsMode_ResolvesForEnterprise()
    {
        // Режим «Аутентификация ОС» в раздельной общей для «1С:Предприятия».
        var ib = FileBase(AuthenticationMode.Prompt, "", "");
        var enterprise = Common("", "", AuthenticationMode.Windows);

        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Enterprise,
            out var mode, out _, out _, enterprise, null, null);

        Assert.Equal(AuthenticationMode.Windows, mode);
    }
}