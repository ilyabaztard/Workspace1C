using System;
using System.IO;
using System.Linq;
using Workspace1C.Converters;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #62 («Иконок конечно маловато, намного меньше, чем в правке структуры Утилиты…
/// Выбираем из того, что есть - сохраняем. Иконка совсем другая»): набор иконок меню трея
/// должен совпадать с каталогом редактора «Утилиты», а выбранная иконка — применяться
/// к группе в трее (не заменяться стандартной). Стражи на каталог, построитель и адаптер.
/// </summary>
public sealed class TrayMenuIconCatalogTests
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
    public void TrayEditor_ReusesUtilitiesIconCatalog()
    {
        // issue #62: редактор меню трея берёт каталог иконок «Утилит» (а не 5 захардкоженных).
        var source = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "TrayMenuEditorViewModel.cs"));

        Assert.Contains("UtilitiesMenuEditorViewModel.BuildCatalogIcons()", source);
    }

    [Fact]
    public void UtilitiesCatalog_HasMoreThanLegacyFive_AndKeysResolveToGeometry()
    {
        // Каталог «Утилит» больше прежних 5 иконок трея; каждый ключ резолвится в геометрию
        // активного набора иконок (иначе в трее показывалась бы «совсем другая» иконка).
        var catalog = UtilitiesMenuEditorViewModel.BuildCatalogIcons();

        Assert.True(catalog.Count > 5, $"Каталог иконок должен быть больше прежних 5 (реально: {catalog.Count})");

        foreach (var option in catalog)
        {
            Assert.NotNull(IconKeyToGeometryConverter.ResolveGeometry(option.Key));
            Assert.StartsWith("#", option.Color);
        }
    }

    [Fact]
    public void Builder_PropagatesIconKeyAndColor_ForUserGroups()
    {
        // Построитель обязан донести ключ и цвет иконки пользовательской группы до адаптеров
        // (WPF рисует выбранную иконку; без IconColor группа теряла бы цвет каталога).
        var layout = new TrayMenuLayout
        {
            Root = new System.Collections.Generic.List<TrayMenuNode>
            {
                new()
                {
                    Kind = "block",
                    BlockKind = TrayMenuBlockKind.UserGroup,
                    CustomTitle = "Группа",
                    IconKey = "Folder",
                    IconColor = "#3B82F6",
                    Style = TrayMenuBlockStyle.Submenu,
                    Visible = true
                }
            }
        };

        var items = new TrayMenuBuilder(k => k).Build(layout);

        var group = items.FirstOrDefault(i => i.Kind == TrayItemKind.Submenu || i.Kind == TrayItemKind.Header);
        Assert.NotNull(group);
        Assert.Equal("Folder", group!.IconKey);
        Assert.Equal("#3B82F6", group.IconColor);
    }

    [Fact]
    public void WpfTrayAdapter_PassesGroupIconKeyAndColor()
    {
        // issue #62 («сохраняем — иконка совсем другая»): WPF-адаптер передаёт выбранную
        // иконку в CreateTrayItem (ранее пункт группы создавался всегда со стандартной).
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Tray.cs"));

        Assert.Contains("TrayIconKind.Group, null, item.IconKey, item.IconColor", code);
        Assert.Contains("DrawCustomTrayIcon(iconKey, iconColor, TrayMenuIconSize)", code);
    }
}