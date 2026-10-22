using System.IO;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #48 «Запуск с аутентификацией»: действия «Запуск с выбором параметров»
/// и «Запуск с аутентификацией» автоматически получают Ctrl+<основной хоткей> и
/// Shift+<основной хоткей> режима (Предприятие F3 → Ctrl+F3/Shift+F3, Конфигуратор
/// F4 → Ctrl+F4/Shift+F4). Если пользователь назначил основной хоткей с Shift или
/// Ctrl — производные не добавляются. Хоткеи показываются в пунктах меню запуска.
/// </summary>
public sealed class LaunchHotkeyDerivativesTests
{
    // ---- Чистая логика производных хоткеев ----

    [Fact]
    public void TryDerive_F3_ReturnsShiftAndCtrl()
    {
        var ok = LaunchHotkeyDerivatives.TryDerive("F3", out var withAuth, out var withParams);

        Assert.True(ok);
        Assert.Equal("Shift+F3", withAuth);
        Assert.Equal("Ctrl+F3", withParams);
    }

    [Fact]
    public void TryDerive_F4_ReturnsShiftAndCtrl()
    {
        var ok = LaunchHotkeyDerivatives.TryDerive("F4", out var withAuth, out var withParams);

        Assert.True(ok);
        Assert.Equal("Shift+F4", withAuth);
        Assert.Equal("Ctrl+F4", withParams);
    }

    [Fact]
    public void TryDerive_AltBase_KeepsAltInDerived()
    {
        var ok = LaunchHotkeyDerivatives.TryDerive("Alt+F4", out var withAuth, out var withParams);

        Assert.True(ok);
        Assert.Equal("Shift+Alt+F4", withAuth);
        Assert.Equal("Ctrl+Alt+F4", withParams);
    }

    [Fact]
    public void TryDerive_DeleteBase_ReturnsDerived()
    {
        var ok = LaunchHotkeyDerivatives.TryDerive("Delete", out var withAuth, out var withParams);

        Assert.True(ok);
        Assert.Equal("Shift+Delete", withAuth);
        Assert.Equal("Ctrl+Delete", withParams);
    }

    [Theory]
    [InlineData("Ctrl+F3")]
    [InlineData("Ctrl+Shift+F3")]
    [InlineData("Shift+F3")]
    public void TryDerive_BaseWithShiftOrCtrl_ReturnsNulls(string baseHotkey)
    {
        var ok = LaunchHotkeyDerivatives.TryDerive(baseHotkey, out var withAuth, out var withParams);

        // Допустимый результат разбора, но производные не нужны: Shift/Ctrl в
        // основном хоткее уже означают «аутентификацию»/«параметры» (условие issue #48).
        Assert.False(ok);
        Assert.Null(withAuth);
        Assert.Null(withParams);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Нет")]
    [InlineData("None")]
    [InlineData("Shift+П")] // символ русской раскладки — не имя клавиши
    [InlineData("Ctrl")]
    public void TryDerive_EmptyOrInvalidBase_ReturnsNulls(string? baseHotkey)
    {
        var ok = LaunchHotkeyDerivatives.TryDerive(baseHotkey, out var withAuth, out var withParams);

        Assert.False(ok);
        Assert.Null(withAuth);
        Assert.Null(withParams);
    }

    [Fact]
    public void TryDerive_IsCaseInsensitive_CanonicalOutput()
    {
        var ok = LaunchHotkeyDerivatives.TryDerive("f3", out var withAuth, out var withParams);

        Assert.True(ok);
        Assert.Equal("Shift+F3", withAuth);
        Assert.Equal("Ctrl+F3", withParams);
    }

    // ---- Дефолты настроек (источник основных хоткеев) ----

    [Fact]
    public void AppSettings_Defaults_EnterpriseF3_ConfiguratorF4()
    {
        var settings = new Models.AppSettings();

        Assert.Equal("F3", settings.HotkeyEnterprise);
        Assert.Equal("F4", settings.HotkeyConfigurator);
    }

    // ---- Пункты меню запуска показывают производные хоткеи (files-based, как
    //      SettingsWindowXamlResourcesTests: читаем исходники проекта) ----

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Workspace1C", "Views")) &&
                Directory.Exists(Path.Combine(dir.FullName, "Workspace1C.Tests")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Корень репозитория не найден из тестового каталога.");
    }

    private static string ReadProjectFile(string relativePath)
    {
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
    }

    [Fact]
    public void MainWindowXaml_MenuItemsShowDerivedGestures_AndConfiguratorAuthItem()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));

        // Жесты производных действий отображаются в пунктах меню запуска (issue #48).
        Assert.Contains("InputGestureText=\"{Binding HotkeyEnterpriseWithParams}\"", xaml);
        Assert.Contains("InputGestureText=\"{Binding HotkeyEnterpriseWithAuth}\"", xaml);
        Assert.Contains("InputGestureText=\"{Binding HotkeyConfiguratorWithParams}\"", xaml);
        Assert.Contains("InputGestureText=\"{Binding HotkeyConfiguratorWithAuth}\"", xaml);
        // У Конфигуратора появился собственный пункт «Запуск с аутентификацией».
        Assert.Contains("LaunchConfiguratorWithAuthCommand", xaml);
    }

    [Fact]
    public void MainWindowAvaloniaTree_SplitMenuShowsGestures_AndConfiguratorAuthItem()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs"));

        Assert.Contains("LaunchConfiguratorWithAuthCommand", source);
        Assert.Contains("HotkeyEnterpriseWithParams", source);
        Assert.Contains("HotkeyEnterpriseWithAuth", source);
        Assert.Contains("HotkeyConfiguratorWithParams", source);
        Assert.Contains("HotkeyConfiguratorWithAuth", source);
        // Жест выводится в пункт меню при наличии (пустая строка не показывается).
        Assert.Contains("item.InputGesture = parsed", source);
    }
}