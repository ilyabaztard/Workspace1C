using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты-стражи диалога повтора удаления информационной базы из кластера (issue #69):
/// у WPF-окна <c>RacInfobaseDropRetryWindow.xaml</c> и Avalonia-построителя
/// <c>RacInfobaseDropRetryWindow.Avalonia.cs</c> есть поля «Логин»/«Пароль»,
/// кнопки «Повторить попытку» (default) и «Отмена» (cancel), а подписи полей
/// используют кисть <c>FormLabelBrush</c> (issue #61). Ключи локализации настроены
/// в ru.json И en.json.
/// </summary>
public sealed class Issue69DropRetryWindowLayoutTests
{
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

    private static string ReadProjectFile(params string[] parts)
    {
        var relativePath = Path.Combine(parts);
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
    }

    [Fact]
    public void WpfWindow_HasLoginPasswordFields_AndRetryCancelButtons()
    {
        // WPF-разметка диалога повтора (issue #69): вместо MessageBox с OK — поля
        // «Логин»/«Пароль», кнопка «Повторить попытку» (IsDefault) и «Отмена» (IsCancel).
        var xaml = ReadProjectFile("Workspace1C", "Views", "RacInfobaseDropRetryWindow.xaml");

        Assert.Contains("LoginBox", xaml, StringComparison.Ordinal);
        Assert.Contains("PasswordBox", xaml, StringComparison.Ordinal);
        Assert.Contains("RetryButton", xaml, StringComparison.Ordinal);
        Assert.Contains("IsDefault=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsCancel=\"True\"", xaml, StringComparison.Ordinal);
        // Подписи полей — кисть FormLabelBrush (issue #61): новое окно сразу с ней.
        Assert.Contains("FormLabelBrush", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void WpfWindow_UsesRetryLocalizationKeys()
    {
        var xaml = ReadProjectFile("Workspace1C", "Views", "RacInfobaseDropRetryWindow.xaml");

        Assert.Contains("ServerMonitor.Infobase.Drop.RetryLogin", xaml, StringComparison.Ordinal);
        Assert.Contains("ServerMonitor.Infobase.Drop.RetryPassword", xaml, StringComparison.Ordinal);
        Assert.Contains("ServerMonitor.Infobase.Drop.RetryButton", xaml, StringComparison.Ordinal);
        Assert.Contains("ServerMonitor.Infobase.Drop.CancelButton", xaml, StringComparison.Ordinal);
        Assert.Contains("ServerMonitor.Infobase.Drop.RetryTitle", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaWindow_HasSameFieldsAndButtons()
    {
        // Avalonia/Linux-построитель (issue #69): те же поля и кнопки, что в WPF-окне;
        // подписи через AvaloniaUi.Label → кисть FormLabelBrush (issue #61).
        var source = ReadProjectFile("Workspace1C", "Views", "RacInfobaseDropRetryWindow.Avalonia.cs");

        Assert.Contains("_loginBox", source, StringComparison.Ordinal);
        Assert.Contains("_passwordBox", source, StringComparison.Ordinal);
        Assert.Contains("ServerMonitor.Infobase.Drop.RetryButton", source, StringComparison.Ordinal);
        Assert.Contains("ServerMonitor.Infobase.Drop.CancelButton", source, StringComparison.Ordinal);
        Assert.Contains("ServerMonitor.Infobase.Drop.RetryLogin", source, StringComparison.Ordinal);
        Assert.Contains("ServerMonitor.Infobase.Drop.RetryPassword", source, StringComparison.Ordinal);
        Assert.Contains("IsDefault = true", source, StringComparison.Ordinal);
        Assert.Contains("IsCancel = true", source, StringComparison.Ordinal);
        Assert.Contains("AvaloniaUi.Label", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Localization_RetryKeys_ExistInRuAndEn_WithText()
    {
        var ru = ReadProjectFile("Workspace1C", "Localization", "Languages", "ru.json");
        var en = ReadProjectFile("Workspace1C", "Localization", "Languages", "en.json");

        Assert.Contains("\"ServerMonitor.Infobase.Drop.RetryTitle\"", ru, StringComparison.Ordinal);
        Assert.Contains("\"ServerMonitor.Infobase.Drop.RetryPrompt\"", ru, StringComparison.Ordinal);
        Assert.Contains("\"ServerMonitor.Infobase.Drop.RetryLogin\"", ru, StringComparison.Ordinal);
        Assert.Contains("\"ServerMonitor.Infobase.Drop.RetryPassword\"", ru, StringComparison.Ordinal);
        Assert.Contains("\"ServerMonitor.Infobase.Drop.RetryButton\"", ru, StringComparison.Ordinal);
        Assert.Contains("\"ServerMonitor.Infobase.Drop.CancelButton\"", ru, StringComparison.Ordinal);

        Assert.Contains("\"ServerMonitor.Infobase.Drop.RetryTitle\"", en, StringComparison.Ordinal);
        Assert.Contains("\"ServerMonitor.Infobase.Drop.RetryPrompt\"", en, StringComparison.Ordinal);
        Assert.Contains("\"ServerMonitor.Infobase.Drop.RetryLogin\"", en, StringComparison.Ordinal);
        Assert.Contains("\"ServerMonitor.Infobase.Drop.RetryPassword\"", en, StringComparison.Ordinal);
        Assert.Contains("\"ServerMonitor.Infobase.Drop.RetryButton\"", en, StringComparison.Ordinal);
        Assert.Contains("\"ServerMonitor.Infobase.Drop.CancelButton\"", en, StringComparison.Ordinal);
    }

    [Fact]
    public void Localization_RussianRetryPrompt_IsHumanReadable()
    {
        // Человекочитаемый русский текст (в тестовой среде LocalizationManager не
        // инициализирован — читаем словарь напрямую, как тесты покрытия локализации).
        using var doc = JsonDocument.Parse(
            ReadProjectFile("Workspace1C", "Localization", "Languages", "ru.json"));
        var strings = doc.RootElement.GetProperty("strings");
        Assert.Contains("администратора", strings.GetProperty("ServerMonitor.Infobase.Drop.RetryPrompt").GetString());
        Assert.Equal("Повторить попытку", strings.GetProperty("ServerMonitor.Infobase.Drop.RetryButton").GetString());
        Assert.Equal("Отмена", strings.GetProperty("ServerMonitor.Infobase.Drop.CancelButton").GetString());
        Assert.Equal("Логин", strings.GetProperty("ServerMonitor.Infobase.Drop.RetryLogin").GetString());
        Assert.Equal("Пароль", strings.GetProperty("ServerMonitor.Infobase.Drop.RetryPassword").GetString());
    }
}