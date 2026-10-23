using System.Collections.Generic;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты каталога контекстного меню базы (issue #56): стандартная компоновка покрывает
/// весь каталог (без дублей), Essential-пункты не теряются, поиск по идентификатору
/// работает, метаданные пунктов заполнены.
/// </summary>
public sealed class ContextMenuCatalogTests
{
    [Fact]
    public void Default_CoversWholeCatalog_WithoutDuplicates()
    {
        var layout = ContextMenuLayouts.Default();
        var ids = new HashSet<string>();
        ContextMenuResolver.CollectItemIds(layout.Root, ids);

        // Дефолтная компоновка содержит ВСЕ пункты каталога ровно по одному разу.
        Assert.Equal(ContextMenuCatalog.All.Count, ids.Count);
        foreach (var item in ContextMenuCatalog.All)
            Assert.Contains(item.Id, ids);
    }

    [Fact]
    public void Default_ContainsEssentialItems()
    {
        // «Пользовательские действия» и «Для выделенных (N)…» нельзя потерять
        // из компоновки (функция 7 и пакетные операции).
        var ids = new HashSet<string>();
        ContextMenuResolver.CollectItemIds(ContextMenuLayouts.Default().Root, ids);

        Assert.Contains("CustomActions", ids);
        Assert.Contains("BatchOperations", ids);
    }

    [Fact]
    public void Default_ContainsStandardGroups()
    {
        var layout = ContextMenuLayouts.Default();

        Assert.Contains(layout.Root, n => n.GroupId == ContextMenuCatalog.GroupUpdateAndLink);
        Assert.Contains(layout.Root, n => n.GroupId == ContextMenuCatalog.GroupAdministration);
        Assert.Contains(layout.Root, n => n.GroupId == ContextMenuCatalog.GroupBackup);
        Assert.Contains(layout.Root, n => n.GroupId == ContextMenuCatalog.GroupClearCache);
    }

    [Fact]
    public void Find_ReturnsMatchingItem()
    {
        var item = ContextMenuCatalog.Find("LaunchEnterprise");

        Assert.NotNull(item);
        Assert.Equal(ContextMenuCommandKind.LaunchEnterprise, item!.CommandKind);
        Assert.Equal("Main.LaunchEnterprise", item.TextKey);
        Assert.NotNull(ContextMenuCatalog.Find("EditSettings"));
    }

    [Fact]
    public void Find_UnknownOrNull_ReturnsNull()
    {
        Assert.Null(ContextMenuCatalog.Find(null));
        Assert.Null(ContextMenuCatalog.Find(string.Empty));
        Assert.Null(ContextMenuCatalog.Find("NonExistentItem"));
    }

    [Fact]
    public void All_ItemsHaveTextKeys_AndKnownIcons()
    {
        // У всех пунктов есть заголовок; иконки непустые, кроме «Актуальные релизы»
        // (в разметке пункт без иконки), и цвет тоже задаётся почти всюду.
        foreach (var item in ContextMenuCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(item.TextKey), $"Пустой TextKey у {item.Id}");
            if (item.Id == "ConfigLink")
                continue;
            Assert.False(string.IsNullOrWhiteSpace(item.WpfIconKind), $"Пустой WpfIconKind у {item.Id}");
            Assert.False(string.IsNullOrWhiteSpace(item.IconColor), $"Пустой IconColor у {item.Id}");
        }
    }

    [Fact]
    public void Catalog_HasNewItem_ForNewsSubmenu()
    {
        // «Создать ярлык» — пункт-новинка: до явного размещения в пользовательской
        // компоновке попадает в «Новинки программы».
        Assert.Contains(ContextMenuCatalog.All, i => i.IsNew && i.Id == "CreateDesktopShortcut");
    }

    [Fact]
    public void GroupIds_HaveTitlesAndIcons()
    {
        Assert.Equal("Menu.UpdateAndLink", ContextMenuCatalog.GroupTitleKey(ContextMenuCatalog.GroupUpdateAndLink));
        Assert.Equal("Menu.Administration", ContextMenuCatalog.GroupTitleKey(ContextMenuCatalog.GroupAdministration));
        Assert.Equal("Menu.Backup", ContextMenuCatalog.GroupTitleKey(ContextMenuCatalog.GroupBackup));
        Assert.Equal("Main.ClearCache", ContextMenuCatalog.GroupTitleKey(ContextMenuCatalog.GroupClearCache));
        Assert.Equal("ContextMenu.Submenu.NewItems", ContextMenuCatalog.GroupTitleKey(ContextMenuCatalog.GroupNewItems));
        // У каждой стандартной группы есть иконка (WPF-ключ и Avalonia-ключ) и цвет.
        foreach (var groupId in new[]
                 {
                     ContextMenuCatalog.GroupUpdateAndLink,
                     ContextMenuCatalog.GroupAdministration,
                     ContextMenuCatalog.GroupBackup,
                     ContextMenuCatalog.GroupClearCache,
                     ContextMenuCatalog.GroupNewItems
                 })
        {
            Assert.False(string.IsNullOrWhiteSpace(ContextMenuCatalog.GroupWpfIconKind(groupId)), groupId);
            Assert.False(string.IsNullOrWhiteSpace(ContextMenuCatalog.GroupAvaloniaIconKey(groupId)), groupId);
            Assert.False(string.IsNullOrWhiteSpace(ContextMenuCatalog.GroupIconColor(groupId)), groupId);
        }
    }

    [Fact]
    public void Layouts_ItemAndSeparator_HelpersProduceExpectedNodes()
    {
        var item = ContextMenuLayouts.Item("LaunchEnterprise");
        Assert.Equal("item", item.Kind);
        Assert.Equal("LaunchEnterprise", item.ItemId);

        var separator = ContextMenuLayouts.Separator();
        Assert.Equal("separator", separator.Kind);
    }
}