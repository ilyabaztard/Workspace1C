using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #67 «Подстановки для сценариев»: сценарий понимает ВСЕ реквизиты базы
/// (включая логин при общей авторизации) + диалог «Выбрать» переменную.
/// </summary>
public sealed class ScriptTokenPickerViewModelTests
{
    private static Infobase BuildBase(string? user = null, string? password = null)
    {
        return new Infobase
        {
            Name = "Бухгалтерия",
            Group = "Бухгалтерия",
            Id = "ib-1",
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.ClientServer,
                Server = "srv-01",
                Port = 1541,
                DatabaseName = "buho",
                User = user ?? string.Empty,
                Password = password ?? string.Empty
            }
        };
    }

    [Fact]
    public void AvailableTokens_IncludesConnectionUser()
    {
        // «Подставить логин оно пока не может» — токен логина теперь в справочнике.
        var tokens = ScriptScenarioEditViewModel.AvailableTokens;

        Assert.Contains(tokens, t => t.Token == "%connection.user%");
    }

    [Fact]
    public void AvailableTokens_IncludesGroupAndId()
    {
        var tokens = ScriptScenarioEditViewModel.AvailableTokens;

        Assert.Contains(tokens, t => t.Token == "%id%");
        Assert.Contains(tokens, t => t.Token == "%имягруппы%");
    }

    [Fact]
    public void BuildValueMap_OwnCredentials_TakesPrecedenceOverResolved()
    {
        var infobase = BuildBase(user: "own-user", password: "own-pass");
        var resolved = ("common-user", "common-pass");

        var map = ScriptParameterResolver.BuildValueMap(infobase, resolved);

        Assert.Equal("own-user", map["connection.user"]);
        Assert.Equal("own-pass", map["connection.password"]);
    }

    [Fact]
    public void BuildValueMap_EmptyOwnCredentials_UsesResolvedLogin()
    {
        // База без собственных учётных данных (общая авторизация issue #57):
        // в карту подставляется логин, который реально подставит стартер.
        var infobase = BuildBase();
        var resolved = ("common-user", "common-pass");

        var map = ScriptParameterResolver.BuildValueMap(infobase, resolved);

        Assert.Equal("common-user", map["connection.user"]);
        Assert.Equal("common-pass", map["connection.password"]);
    }

    [Fact]
    public void TokenPicker_BuildOptions_IncludesStaticAndDynamicTokens()
    {
        var infobase = BuildBase();
        var options = ScriptTokenPickerViewModel.BuildOptions(infobase);

        // Статический токен логина есть; динамический ключ карты (например id) тоже.
        Assert.Contains(options, o => o.Token == "%connection.user%");
        Assert.Contains(options, o => o.Token == "%id%");
    }

    [Fact]
    public void TokenPicker_FilterOptions_MatchesTokenAndValue_IgnoreCase()
    {
        var infobase = BuildBase();
        var all = ScriptTokenPickerViewModel.BuildOptions(infobase);

        var byToken = ScriptTokenPickerViewModel.FilterOptions(all, "connection.server");
        var byValue = ScriptTokenPickerViewModel.FilterOptions(all, "srv-01");

        Assert.Contains(byToken, o => o.Token == "%connection.server%");
        Assert.Contains(byValue, o => o.Value == "srv-01");
    }

    [Fact]
    public void TokenPicker_FilterOptions_EmptyQuery_ReturnsAll()
    {
        var infobase = BuildBase();
        var all = ScriptTokenPickerViewModel.BuildOptions(infobase);

        var filtered = ScriptTokenPickerViewModel.FilterOptions(all, "   ");

        Assert.Equal(all.Count, filtered.Count);
    }

    [Fact]
    public void TokenOption_PasswordTokens_MaskedInDisplay()
    {
        var option = new ScriptTokenOption("%connection.password%", "secret");

        Assert.True(option.IsSecret);
        Assert.Equal("••••••", option.DisplayValue);
    }
}