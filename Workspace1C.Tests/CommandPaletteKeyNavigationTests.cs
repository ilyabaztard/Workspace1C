using System;
using System.IO;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Разметочные стражи issue #84 «Не обрабатываются клавиши Up и Down в поле поиска
/// командной палитры»: WPF-разметка подключает к поисковой строке PreviewKeyDown
/// (а не KeyDown — восходящее событие до обработчика не доходит, TextBox помечает
/// стрелки Handled), Avalonia-версия подписывается на KeyDownEvent с
/// RoutingStrategies.Tunnel (аналог PreviewKeyDown). Обе платформы используют ЕДИНУЮ
/// логику навигации <c>MoveSelection</c> ViewModel и не переводят фокус на список.
/// </summary>
public sealed class CommandPaletteKeyNavigationTests
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
    public void Wpf_SearchBox_UsesPreviewKeyDown_NotBubblingKeyDown()
    {
        // issue #84: стрелки Up/Down перехватываются внутренней обработкой TextBox
        // (помечаются Handled), поэтому обработчик висит на PreviewKeyDown (tunneling,
        // «до» внутренней обработки), а не на KeyDown (bubbling, «после»).
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "CommandPaletteWindow.xaml"));

        Assert.Contains("PreviewKeyDown=\"OnSearchBox_PreviewKeyDown\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("KeyDown=\"OnSearchBox_KeyDown\"", xaml, StringComparison.Ordinal);
        // Обработчик висит именно на поисковой строке (фокус остаётся в ней).
        var searchTextBoxLine = xaml.IndexOf("x:Name=\"SearchBox\"", StringComparison.Ordinal);
        Assert.True(searchTextBoxLine >= 0, "В разметке должна быть поисковая строка SearchBox.");
        Assert.Contains("OnSearchBox_PreviewKeyDown", xaml.Substring(searchTextBoxLine, 300), StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_Handler_UsesSharedMoveSelection_AndKeepsFocusInSearchBox()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "CommandPaletteWindow.xaml.cs"));

        Assert.Contains("private void OnSearchBox_PreviewKeyDown", code, StringComparison.Ordinal);
        // Единая логика навигации обеих платформ — метод ViewModel MoveSelection.
        Assert.Contains("_palette.MoveSelection(+1)", code, StringComparison.Ordinal);
        Assert.Contains("_palette.MoveSelection(-1)", code, StringComparison.Ordinal);
        Assert.Contains("e.Handled = true;", code, StringComparison.Ordinal);
        // Фокус не покидает поисковую строку: обработчик не переводит его на список.
        Assert.DoesNotContain("ItemList.Focus(", code, StringComparison.Ordinal);
        Assert.DoesNotContain("MoveFocus(", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_Handler_AttachedAsTunnel_LikePreviewKeyDown()
    {
        // issue #84 (Avalonia): у TextBox стрелки Up/Down тоже помечаются Handled
        // внутренней обработкой — подписка на KeyDownEvent с RoutingStrategies.Tunnel
        // приходит «до» неё (аналог WPF PreviewKeyDown).
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "CommandPaletteWindow.Avalonia.cs"));

        Assert.Contains("InputElement.KeyDownEvent", code, StringComparison.Ordinal);
        Assert.Contains("OnSearchBox_PreviewKeyDown", code, StringComparison.Ordinal);
        Assert.Contains("RoutingStrategies.Tunnel", code, StringComparison.Ordinal);
        // Той же единой логикой навигации, что и WPF:
        Assert.Contains("_palette.MoveSelection(+1)", code, StringComparison.Ordinal);
        Assert.Contains("_palette.MoveSelection(-1)", code, StringComparison.Ordinal);
        Assert.Contains("e.Handled = true;", code, StringComparison.Ordinal);
        // Фокус остаётся в поле поиска — на список не переводится.
        Assert.DoesNotContain("_itemList.Focus(", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ViewModel_HasSharedMoveSelection_UsedByBothPlatforms()
    {
        // Единая навигация: обе платформы зовут MoveSelection(-1/+1); границы списка
        // не зацикливаются (прежнее поведение MoveUp/MoveDown — «зажатие»).
        var code = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "CommandPaletteViewModel.cs"));

        Assert.Contains("public bool MoveSelection(int delta)", code, StringComparison.Ordinal);
        Assert.Contains("return MoveUp();", code, StringComparison.Ordinal);
        Assert.Contains("return MoveDown();", code, StringComparison.Ordinal);
    }
}
