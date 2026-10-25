using System;
using System.IO;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #62 «Правка меню трея» (замечание 7OH: «Блоки, добавленные в группу, перестают
/// функционировать»): стражи на исходники адаптеров WPF и Avalonia, чтобы вложенные
/// блоки в группе снова не превратились в «мёртвые» пункты без динамического
/// содержимого и обработчиков (по образцу Issue54EscapeCloseTests/Issue61TitleColorsTests).
/// </summary>
public sealed class TrayMenuNestedAdapterContractTests
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
    public void Wpf_AppendChildItems_DispatchesNestedHeadersAndSubmenus()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Tray.cs"));

        // WPF-адаптер «дети пользовательской группы» обязан маршрутизировать вложенные
        // блоки (Header/Submenu) в content-методы с полной динамикой, а не создавать
        // пункты без обработчиков.
        Assert.Contains("case TrayItemKind.Header:", code);
        Assert.Contains("case TrayItemKind.Submenu:", code);
        Assert.Contains("AppendTrayBlockContent(owner, toolStrip, child)", code);
        Assert.Contains("AppendTraySubmenuContent(owner, toolStrip, child)", code);
        // Вложенный динамический контент использует AttachLaunchSubmenu (клики запуска).
        Assert.Contains("AttachLaunchSubmenu(favoriteItem, toolStrip, id)", code);
        Assert.Contains("AttachLaunchSubmenu(baseItem, submenu.DropDown, id)", code);
    }

    [Fact]
    public void Avalonia_AppendChildItems_DispatchesNestedHeadersAndSubmenus()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Tray.cs"));

        Assert.Contains("case TrayItemKind.Separator:", code);
        Assert.Contains("case TrayItemKind.Header:", code);
        Assert.Contains("case TrayItemKind.Submenu:", code);
        // Вложенные блоки идут через штатные адаптеры (умеют Favorites/Recent/Services).
        Assert.Contains("AppendTrayBlock(owner, child)", code);
        Assert.Contains("AppendTraySubmenu(owner, child)", code);
    }

    [Fact]
    public void Builder_SubmenuChildren_PreserveBlockKinds()
    {
        // Чистый построитель кладёт в Children вложенных блоков именно Header/Submenu
        // с Block — адаптеры должны их различать (по образцу TrayMenuGroupMoveTests).
        var layout = Workspace1C.Services.TrayMenuLayouts.Default();
        var items = new Workspace1C.Services.TrayMenuBuilder(_ => string.Empty).Build(layout);

        foreach (var submenu in items.FindAll(i => i.Kind == Workspace1C.Services.TrayItemKind.Submenu))
        {
            foreach (var child in submenu.Children)
            {
                Assert.True(
                    child.Kind == Workspace1C.Services.TrayItemKind.Separator ||
                    child.Kind == Workspace1C.Services.TrayItemKind.Header ||
                    child.Kind == Workspace1C.Services.TrayItemKind.Submenu,
                    $"Неожиданный вид вложенного пункта меню трея: {child.Kind}");
            }
        }
    }
}