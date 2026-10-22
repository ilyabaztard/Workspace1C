using System.IO;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #47 «Закрытие по ESC»: формы должны закрываться по Esc. Файловые проверки
/// исходников окон (по образцу SettingsWindowXamlResourcesTests) — страховка, что у
/// WPF-версий окна/диалога есть кнопка IsCancel или явный обработчик Esc, а у
/// Avalonia-версий — закрытие по Escape в коде окна сообщений.
/// </summary>
public sealed class EscCloseWindowsTests
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

    private static string ReadProjectFile(string relativePath)
    {
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
    }

    [Fact]
    public void UsageStatisticsWindow_HasCancelButton()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "UsageStatisticsWindow.xaml"));
        Assert.Contains("IsCancel=\"True\"", xaml);
    }

    [Fact]
    public void FindLostBasesWindow_HasCancelButton()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "FindLostBasesWindow.xaml"));
        Assert.Contains("IsCancel=\"True\"", xaml);
    }

    [Fact]
    public void ProcessInspectorWindow_HasCancelButton()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ProcessInspectorWindow.xaml"));
        Assert.Contains("IsCancel=\"True\"", xaml);
    }

    [Fact]
    public void RepositoryBatchUpdateWindow_HasCancelButton()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "RepositoryBatchUpdateWindow.xaml"));
        Assert.Contains("IsCancel=\"True\"", xaml);
    }

    [Fact]
    public void MaterialMessageWindow_HasCancelButton_AndEscHandler()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Services", "MaterialMessageWindow.xaml"));
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Services", "MaterialMessageWindow.xaml.cs"));

        Assert.Contains("IsCancel=\"True\"", xaml);
        // Для информационных окон (кнопка «Нет» скрыта) ESC обрабатывается в коде.
        Assert.Contains("Key.Escape", code);
    }

    [Fact]
    public void MaterialMessageWindowAvalonia_ClosesByEscape()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Services", "MaterialMessageWindow.Avalonia.cs"));
        Assert.Contains("Key.Escape", source);
    }

    [Fact]
    public void ModalDialogWindows_InheritModalWindowBaseOnLinux()
    {
        // Пять указанных в issue #47 окон на Linux наследуют ModalWindowBase,
        // который закрывает диалог по Esc (первый ESC — подсказки, второй — окно).
        var cases = new[] { "UsageStatisticsWindow", "FindLostBasesWindow", "ProcessInspectorWindow", "RepositoryBatchUpdateWindow" };
        foreach (var name in cases)
        {
            var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", $"{name}.Avalonia.cs"));
            Assert.Contains(": ModalWindowBase", source);
        }
    }
}