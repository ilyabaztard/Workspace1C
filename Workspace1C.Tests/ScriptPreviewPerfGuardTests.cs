using System;
using System.IO;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #67 (замечание 7OH: «при правке шаблона нажимаешь пробел и 2-3 секунды тормозов;
/// первый вход в редактор вообще около 25 секунд»): стражи на окна редактора сценария —
/// превью обязано переиспользовать закэшированный контекст платформы для «Базы для
/// примера» и пересчитываться с задержкой, а не вызывать резолвер на каждый символ
/// (по образцу Issue54EscapeCloseTests/Issue61TitleColorsTests).
/// </summary>
public sealed class ScriptPreviewPerfGuardTests
{
    private static string ReadProjectFile(string relativePath)
    {
        var root = FindRepoRoot();
        var fullPath = Path.Combine(root, relativePath);
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Fact]
    public void Wpf_EditorWindow_CachesLaunchContextForExampleBase()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ScriptScenarioEditWindow.xaml.cs"));

        // Контекст платформы вычисляется один раз на выбранную «Базу для примера» и
        // передаётся в пересчёт превью (резолвер НЕ вызывается на каждое нажатие клавиши).
        Assert.Contains("_exampleLaunchContext", code);
        Assert.Contains("ScriptLaunchContextResolver.Resolve(SelectedExampleBase)", code);
        Assert.Contains("BuildExampleCommandLine(draft, SelectedExampleBase, null, _exampleLaunchContext)", code);
    }

    [Fact]
    public void Avalonia_EditorWindow_CachesLaunchContextForExampleBase()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ScriptScenarioEditWindow.Avalonia.cs"));

        Assert.Contains("_exampleLaunchContext", code);
        Assert.Contains("ScriptLaunchContextResolver.Resolve(SelectedExampleBase)", code);
        Assert.Contains("BuildExampleCommandLine(draft, SelectedExampleBase, null, _exampleLaunchContext)", code);
    }

    [Fact]
    public void Wpf_EditorWindow_DebouncesPreviewRebuild()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ScriptScenarioEditWindow.xaml.cs"));

        // Пересчёт превью планируется таймером (300 мс), а не выполняется на каждый символ.
        Assert.Contains("_previewDebounceTimer", code);
        Assert.Contains("System.Windows.Threading", code);
    }

    [Fact]
    public void Avalonia_EditorWindow_DebouncesPreviewRebuild()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ScriptScenarioEditWindow.Avalonia.cs"));

        Assert.Contains("_previewDebounceTimer", code);
        Assert.Contains("using Avalonia.Threading", code);
    }

    [Fact]
    public void Wpf_EditorAndPicker_PassInitialBaseFromLaunchedBase()
    {
        // Issue #67 (п.4): «База для примера» — база, из которой запущен сценарий.
        var editor = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ScriptScenarioEditWindow.xaml.cs"));
        var pick = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ScriptPickWindow.xaml.cs"));

        Assert.Contains("Infobase? initialBase = null", editor);
        Assert.Contains("_initialBase", editor);
        Assert.Contains("new ScriptScenarioEditWindow(item.Scenario, _infobase)", pick);
    }

    [Fact]
    public void Avalonia_EditorAndPicker_PassInitialBaseFromLaunchedBase()
    {
        var editor = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ScriptScenarioEditWindow.Avalonia.cs"));
        var pick = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ScriptPickWindow.Avalonia.cs"));

        Assert.Contains("Infobase? initialBase = null", editor);
        Assert.Contains("_initialBase", editor);
        Assert.Contains("new ScriptScenarioEditWindow(item.Scenario, _infobase)", pick);
    }
}