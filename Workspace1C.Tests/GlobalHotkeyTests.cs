using System;
using System.IO;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #82 «Горячие клавиши для открытия окна программы»: настройка глобальной
/// клавиши хранится в AppSettings (по умолчанию Ctrl+Alt+W), комбинация валидируется
/// платформонезависимым <see cref="GlobalHotkeyLogic"/>, а главное окно обеих платформ
/// регистрирует нативный хук (RegisterHotKey / X11-GrabKey) и при нажатии восстанавливает
/// окно из трея/свёрнутого состояния. Разметочные части стражей читают исходники.
/// </summary>
public sealed class GlobalHotkeyTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ReadProjectFile(string relativePath)
    {
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
    }

    [Fact]
    public void DefaultGesture_IsValidAndParsesToAltControl()
    {
        // Дефолт Ctrl+Alt+W — валидное сочетание с модификаторами (обязательно для
        // глобального захвата, иначе можно конфликтовать с печатью в любом приложении).
        Assert.Equal("Ctrl+Alt+W", GlobalHotkeyLogic.DefaultGesture);
        Assert.True(GlobalHotkeyLogic.IsValid(GlobalHotkeyLogic.DefaultGesture));
        Assert.True(HotkeyMatcher.TryParse(GlobalHotkeyLogic.DefaultGesture, out var key, out var mods));
        Assert.Equal("W", key);
        Assert.True((mods & HotkeyModifiers.Control) != 0 && (mods & HotkeyModifiers.Alt) != 0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("F5")]              // без модификатора — глобальный захват не регистрируем
    [InlineData("СтраннаяКлавиша")]
    public void Normalize_InvalidGesture_ReturnsEmpty(string gesture)
    {
        Assert.Equal(string.Empty, GlobalHotkeyLogic.Normalize(gesture));
    }

    [Fact]
    public void Normalize_ValidGesture_KeepsTrimmed()
    {
        Assert.Equal("Ctrl+Shift+F9", GlobalHotkeyLogic.Normalize("  Ctrl+Shift+F9  "));
    }

    [Fact]
    public void AppSettings_GlobalShowWindowHotkey_HasDefault()
    {
        // Значение по умолчанию задано в модели — старые settings.json без поля
        // получают клавишу Ctrl+Alt+W, а не «пусто».
        Assert.Equal("Ctrl+Alt+W", new AppSettings().GlobalShowWindowHotkey);
    }

    [Fact]
    public void WpfSettings_HasShowWindowHotkeyField()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.xaml"));
        Assert.Contains("Settings.Hotkeys.ShowWindow", xaml, StringComparison.Ordinal);
        Assert.Contains("GlobalShowWindowHotkeyBox", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void WpfMainWindow_RegistersGlobalHotkey()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Hotkeys.cs"));
        Assert.Contains("RegisterGlobalShowWindowHotkey", code, StringComparison.Ordinal);
        Assert.Contains("new GlobalHotkeyServiceWindows(this)", code, StringComparison.Ordinal);
        Assert.Contains("RestoreFromTrayPublic", code, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaMainWindow_RegistersGlobalHotkey()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.cs"));
        Assert.Contains("RegisterGlobalShowWindowHotkey", code, StringComparison.Ordinal);
        Assert.Contains("new Services.GlobalHotkeyServiceAvalonia()", code, StringComparison.Ordinal);
        Assert.Contains("ShowAndActivate", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Platforms_HaveNativeGlobalHotkeyImplementations()
    {
        var win = ReadProjectFile(Path.Combine("Workspace1C", "Services", "GlobalHotkeyService.Windows.cs"));
        Assert.Contains("RegisterHotKey", win, StringComparison.Ordinal);
        Assert.Contains("WmHotkey", win, StringComparison.Ordinal);

        var linux = ReadProjectFile(Path.Combine("Workspace1C", "Services", "GlobalHotkeyService.Avalonia.cs"));
        Assert.Contains("XGrabKey", linux, StringComparison.Ordinal);
        Assert.Contains("XNextEvent", linux, StringComparison.Ordinal);
    }
}