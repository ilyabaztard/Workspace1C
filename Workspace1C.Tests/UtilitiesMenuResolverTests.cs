using System;
using System.IO;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты резолвера компоновки меню «Утилиты» (issue #374): выбор стандартной компоновки
/// при выключенной правке и пользовательской при включённой; сбор «Новинок программы».
/// </summary>
public sealed class UtilitiesMenuResolverTests
{
    [Fact]
    public void Resolve_EditingDisabled_ReturnsDefaultLayout()
    {
        var settings = new AppSettings { UtilitiesMenuEditingEnabled = false };
        var layout = UtilitiesMenuResolver.Resolve(settings, null);
        Assert.NotNull(layout);
        Assert.Equal("CommandPalette", layout!.Root[0].ItemId);
    }

    [Fact]
    public void Resolve_EditingDisabled_IgnoresStore()
    {
        var settings = new AppSettings { UtilitiesMenuEditingEnabled = false };
        var store = new MemoryLayoutStore(new UtilitiesMenuLayout
        {
            Root = new System.Collections.Generic.List<UtilitiesMenuNode>
            {
                UtilitiesMenuLayouts.Item("ActualReleases")
            }
        });
        var layout = UtilitiesMenuResolver.Resolve(settings, store);
        // Правка выключена — файл компоновки игнорируется.
        Assert.Equal("CommandPalette", layout!.Root[0].ItemId);
    }

    [Fact]
    public void Resolve_EditingEnabled_NoStore_ReturnsDefaultLayout()
    {
        var settings = new AppSettings { UtilitiesMenuEditingEnabled = true };
        var layout = UtilitiesMenuResolver.Resolve(settings, null);
        Assert.NotNull(layout);
        // 12 пунктов/групп + 5 узлов-разделителей (issue #374; «Управление службами»
        // issue #11 добавлено после «Серверы 1С»).
        Assert.Equal(17, layout!.Root.Count);
    }

    [Fact]
    public void Resolve_EditingEnabled_WithStore_ReturnsCustomLayout()
    {
        var settings = new AppSettings { UtilitiesMenuEditingEnabled = true };
        var custom = new UtilitiesMenuLayout
        {
            Root = new System.Collections.Generic.List<UtilitiesMenuNode>
            {
                UtilitiesMenuLayouts.Item("ActualReleases"),
                UtilitiesMenuLayouts.Item("ServerMonitor", separatorBefore: true)
            }
        };
        var store = new MemoryLayoutStore(custom);
        var layout = UtilitiesMenuResolver.Resolve(settings, store);
        Assert.NotNull(layout);
        Assert.Equal(2, layout!.Root.Count);
        Assert.Equal("ActualReleases", layout.Root[0].ItemId);
        Assert.Equal("ServerMonitor", layout.Root[1].ItemId);
        Assert.True(layout.Root[1].SeparatorBefore);
    }

    [Fact]
    public void Resolve_NullSettings_ReturnsDefaultLayout()
    {
        var layout = UtilitiesMenuResolver.Resolve(null, null);
        Assert.NotNull(layout);
        Assert.Equal("CommandPalette", layout!.Root[0].ItemId);
    }

    [Fact]
    public void CollectNewItems_CurrentCatalog_HasNoUnplacedNewItems()
    {
        // В каталоге сейчас нет пунктов с признаком «новинка» (все пункты уже размещены
        // в стандартной компоновке), поэтому «Новинки программы» пустые.
        var layout = UtilitiesMenuLayouts.Default();
        var newItems = UtilitiesMenuResolver.CollectNewItems(layout);
        Assert.Empty(newItems);
    }

    [Fact]
    public void CollectNewItems_NullLayout_ReturnsAllNewItems()
    {
        // null-компоновка = «ничего не размещено»: возвращаются все пункты каталога
        // с признаком «новинка» (сейчас это «Уведомления: Telegram и email», 0.3.12.69).
        var newItems = UtilitiesMenuResolver.CollectNewItems(null);

        Assert.Contains(newItems, i => i.Id == "NotificationChannels");
    }

    [Fact]
    public void CollectItemIds_CollectsItemsAcrossGroups()
    {
        var layout = UtilitiesMenuLayouts.Default();
        var ids = new System.Collections.Generic.HashSet<string>();
        UtilitiesMenuResolver.CollectItemIds(layout.Root, ids);

        Assert.Contains("CommandPalette", ids);
        Assert.Contains("CheckForUpdates", ids);
        // Пункты внутри групп тоже собираются.
        Assert.Contains("UnlockPrivateBases", ids);
        Assert.Contains("ConfigDiff", ids);
        Assert.Contains("RemoveMissingFileBases", ids);
        // Идентификаторы не дублируются: HashSet.
        Assert.True(ids.Count > 10);
    }

    // ---- «Новинки программы» в структуре (issue #4) ----

    [Fact]
    public void EnsureNewItemsGroup_NoNewItems_ReturnsSameLayout()
    {
        // Стандартная компоновка содержит все пункты каталога — «Новинки» не добавляются.
        var layout = UtilitiesMenuLayouts.Default();
        var result = UtilitiesMenuResolver.EnsureNewItemsGroup(layout);

        Assert.Same(layout, result);
        Assert.NotNull(result);
        Assert.DoesNotContain(result.Root,
            n => string.Equals(n.GroupId, UtilitiesMenuCatalog.GroupNewItems, StringComparison.Ordinal));
    }

    [Fact]
    public void EnsureNewItemsGroup_AddsGroupWithNewItemsAtEnd()
    {
        // Пользовательская компоновка без пункта-новинки: группа создаётся в конце корня
        // с разделителем перед ней и принудительно видимой.
        var layout = new UtilitiesMenuLayout
        {
            Root = new System.Collections.Generic.List<UtilitiesMenuNode>
            {
                UtilitiesMenuLayouts.Item("CommandPalette")
            }
        };

        var result = UtilitiesMenuResolver.EnsureNewItemsGroup(layout);

        Assert.NotNull(result);
        var group = result.Root[^1];
        Assert.Equal("group", group.Kind);
        Assert.Equal(UtilitiesMenuCatalog.GroupNewItems, group.GroupId);
        Assert.True(group.Visible);
        Assert.True(group.SeparatorBefore);
        Assert.Contains(group.Children, c => c.ItemId == "NotificationChannels");
    }

    [Fact]
    public void EnsureNewItemsGroup_ExistingGroup_AddsMissingItems()
    {
        // Группа уже есть в компоновке (пользователь мог её переместить) — пункты
        // добавляются в неё, а не в новую группу в конце корня.
        var group = new UtilitiesMenuNode
        {
            Kind = "group",
            GroupId = UtilitiesMenuCatalog.GroupNewItems,
            Visible = true,
            Children = new System.Collections.Generic.List<UtilitiesMenuNode>()
        };
        var layout = new UtilitiesMenuLayout
        {
            Root = new System.Collections.Generic.List<UtilitiesMenuNode>
            {
                UtilitiesMenuLayouts.Item("CommandPalette"),
                group
            }
        };

        var result = UtilitiesMenuResolver.EnsureNewItemsGroup(layout);

        Assert.NotNull(result);
        Assert.Same(group, result.Root[1]);
        Assert.Contains(group.Children, c => c.ItemId == "NotificationChannels");
    }

    [Fact]
    public void EnsureNewItemsGroup_ExistingHiddenGroup_ForcesVisible()
    {
        // Скрытие группы в файле не влияет на отображение при работе: видимость
        // принудительно включается (issue #4).
        var group = new UtilitiesMenuNode
        {
            Kind = "group",
            GroupId = UtilitiesMenuCatalog.GroupNewItems,
            Visible = false,
            Children = new System.Collections.Generic.List<UtilitiesMenuNode>()
        };
        var layout = new UtilitiesMenuLayout
        {
            Root = new System.Collections.Generic.List<UtilitiesMenuNode> { group }
        };

        var result = UtilitiesMenuResolver.EnsureNewItemsGroup(layout);

        Assert.NotNull(result);
        Assert.True(result.Root[0].Visible);
        Assert.Contains(result.Root[0].Children, c => c.ItemId == "NotificationChannels");
    }

    [Fact]
    public void EnsureNewItemsGroup_NullLayout_ReturnsNull()
    {
        Assert.Null(UtilitiesMenuResolver.EnsureNewItemsGroup(null));
    }

    [Fact]
    public void EnsureNewItemsGroup_ExistingEmptyGroup_BecomesHidden()
    {
        // Все пункты каталога размещены в компоновке — новых нет. Группа «Новинки
        // программы» в рабочем меню скрывается, даже если в файле она помечена
        // видимой: видимость определяется наличием пунктов, а не галочкой (issue #4).
        var layout = UtilitiesMenuLayouts.Default();
        var group = new UtilitiesMenuNode
        {
            Kind = "group",
            GroupId = UtilitiesMenuCatalog.GroupNewItems,
            Visible = true,
            Children = new System.Collections.Generic.List<UtilitiesMenuNode>()
        };
        layout.Root.Add(group);

        var result = UtilitiesMenuResolver.EnsureNewItemsGroup(layout);

        Assert.NotNull(result);
        Assert.False(result.Root[^1].Visible);
        Assert.Empty(result.Root[^1].Children);
    }

    [Fact]
    public void EnsureNewItemsGroup_NoNewItems_NoGroup_ReturnsSameLayout()
    {
        // Новых пунктов нет и группы нет — компоновка не меняется (issue #4).
        var layout = UtilitiesMenuLayouts.Default();

        var result = UtilitiesMenuResolver.EnsureNewItemsGroup(layout);

        Assert.Same(layout, result);
    }

    /// <summary>Хранилище-заглушка для тестов (без файла на диске).</summary>
    private sealed class MemoryLayoutStore : IUtilitiesMenuLayoutStore
    {
        private readonly UtilitiesMenuLayout? _layout;

        public MemoryLayoutStore(UtilitiesMenuLayout? layout)
        {
            _layout = layout;
        }

        public string FilePath => Path.Combine(Path.GetTempPath(), "memory_utilities_menu_layout.json");

        public UtilitiesMenuLayout? Load() => _layout;

        public void Save(UtilitiesMenuLayout layout)
        {
            // Не сохраняем: тестовая заглушка.
        }
    }
}