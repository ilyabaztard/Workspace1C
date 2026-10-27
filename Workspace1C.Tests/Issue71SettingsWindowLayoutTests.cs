using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Разметочные тесты окна «Настройки» и «Настройка информационной базы» (issue #71,
/// последний комментарий 7OH): после фикса #55 настроечные вкладки «размахнулись» —
/// многие элементы стали шире окна и почти на всех вкладках появилась горизонтальная
/// прокрутка. Принята единая с #55 стратегия «минимальные размеры + запрет сужения»:
/// окнам заданы MinWidth/MinHeight, при которых всё содержимое вкладок помещается по
/// ширине, а горизонтальная прокрутка вкладок отключена (Disabled).
/// Тесты читают исходники из репозитория (как Issue55WindowLayoutTests) и поэтому
/// работают на обеих платформах без запуска UI.
/// </summary>
public sealed class Issue71SettingsWindowLayoutTests
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

    private static int Count(string text, string needle) =>
        Regex.Matches(text, Regex.Escape(needle)).Count;

    // ------------------------------------------------------------------
    // Минимальные размеры окон настроек (issue #71): сузить окно мышкой ниже
    // MinWidth/MinHeight нельзя, поэтому содержимое вкладок не «уезжает»
    // за правый край и горизонтальная прокрутка не появляется.
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("SettingsWindow.xaml", "960", "720")]
    [InlineData("ConnectionSettingsWindow.xaml", "760", "780")]
    public void WpfSettingsWindows_HaveMinSizesPreventingShrink(string fileName, string minWidth, string minHeight)
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", fileName));

        Assert.Contains("ResizeMode=\"CanResizeWithGrip\"", xaml, StringComparison.Ordinal);
        Assert.Contains($"MinWidth=\"{minWidth}\"", xaml, StringComparison.Ordinal);
        Assert.Contains($"MinHeight=\"{minHeight}\"", xaml, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SettingsWindow.Avalonia.cs", "960", "720")]
    [InlineData("ConnectionSettingsWindow.Avalonia.cs", "760", "780")]
    public void AvaloniaSettingsWindows_HaveMinSizesPreventingShrink(string fileName, string minWidth, string minHeight)
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", fileName));

        Assert.Contains($"MinWidth = {minWidth};", code, StringComparison.Ordinal);
        Assert.Contains($"MinHeight = {minHeight};", code, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // Горизонтальная прокрутка вкладок настроек отключена (issue #71):
    // контентные ScrollViewer вкладок имеют Disabled, вертикальная полоса
    // остаётся. Auto допустима только у вложенных списков/просмотрщиков.
    // ------------------------------------------------------------------

    [Fact]
    public void SettingsWindow_Wpf_TabContentHasNoHorizontalScroll()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.xaml"));

        // 17 контентных ScrollViewer вкладок + полоса списка вкладок слева — Disabled.
        Assert.True(
            Count(xaml, "HorizontalScrollBarVisibility=\"Disabled\"") >= 18,
            "Контентные ScrollViewer вкладок должны иметь горизонтальную полосу Disabled (issue #71).");
        // Единственная Auto — вложенная полоса списка каталогов шаблонов (данные списка).
        Assert.Equal(1, Count(xaml, "HorizontalScrollBarVisibility=\"Auto\""));
    }

    [Fact]
    public void SettingsWindow_Avalonia_TabContentHasNoHorizontalScroll()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.Avalonia.cs"));

        // Контентные ScrollViewer вкладок — Disabled; единственная Auto — отдельное
        // окно просмотра картинки «О программе» (donat.png), не раскладка вкладок.
        Assert.True(
            Count(code, "HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled") >= 10,
            "Контентные ScrollViewer вкладок должны иметь горизонтальную полосу Disabled (issue #71).");
        Assert.Equal(1, Count(code, "HorizontalScrollBarVisibility = ScrollBarVisibility.Auto"));
    }

    [Fact]
    public void ConnectionSettingsWindow_Wpf_TabsHaveNoHorizontalScroll()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ConnectionSettingsWindow.xaml"));

        // 8 вкладок, каждая обёрнута в собственный ScrollViewer без горизонтальной полосы.
        Assert.Equal(8, Count(xaml, "<ScrollViewer"));
        Assert.Equal(8, Count(xaml, "HorizontalScrollBarVisibility=\"Disabled\""));
        Assert.Equal(0, Count(xaml, "HorizontalScrollBarVisibility=\"Auto\""));
    }

    [Fact]
    public void ConnectionSettingsWindow_Avalonia_TabsHaveNoHorizontalScroll()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ConnectionSettingsWindow.Avalonia.cs"));

        // Общая обёртка вкладок (метод Tab) горизонтальной прокрутки не имеет.
        Assert.Contains("HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled", code, StringComparison.Ordinal);
        Assert.DoesNotContain("HorizontalScrollBarVisibility = ScrollBarVisibility.Auto", code, StringComparison.Ordinal);
    }
}
