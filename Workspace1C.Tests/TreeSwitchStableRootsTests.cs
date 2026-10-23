using System.Collections.Specialized;
using Workspace1C.Collections;
using Workspace1C.Models;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты стабильного набора корней дерева (issue #21, Этап F): GroupNodes всегда содержит
/// плоский узел и все корни групп, а переключение вкладки меняет только видимость узлов
/// (<see cref="GroupNodeViewModel.IsVisible"/>), а не состав коллекции. Так контейнеры
/// корней не пересоздаются (нет Reset GroupNodes, collReset=0 в CM_TREESWITCH) — источник
/// роста heapAfterGcMb и задержки переключения вкладок.
/// </summary>
public sealed class TreeSwitchStableRootsTests
{
    private static Infobase CreateBase(string name) => new()
    {
        Name = name,
        Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"C:\bases\" + name }
    };

    private static GroupNodeViewModel CreateGroup(string id, string name, params Infobase[] bases)
    {
        var node = new GroupNodeViewModel(new Group { Id = id, Name = name });
        foreach (var ib in bases)
            node.Infobases.Add(ib);
        node.PopulateItems();
        return node;
    }

    /// <summary>Строит стабильный набор корней: плоский узел + две группы.</summary>
    private static (ObservableRangeCollection<GroupNodeViewModel> GroupNodes, GroupNodeViewModel Flat, GroupNodeViewModel G1, GroupNodeViewModel G2)
        BuildStableRoots()
    {
        var flat = new GroupNodeViewModel(null);
        var g1 = CreateGroup("g1", "Группа 1", CreateBase("База 1"));
        var g2 = CreateGroup("g2", "Группа 2", CreateBase("База 2"));
        var groupNodes = new ObservableRangeCollection<GroupNodeViewModel> { flat, g1, g2 };
        return (groupNodes, flat, g1, g2);
    }

    /// <summary>
    /// Чистое переключение вкладки при стабильном наборе корней не порождает событий
    /// коллекции вовсе (в т.ч. Reset): состав GroupNodes не меняется, меняется только
    /// видимость узлов. Это и есть критерий collReset=0 в CM_TREESWITCH.
    /// </summary>
    [Fact]
    public void ModeSwitch_StableComposition_NoCollectionEvents()
    {
        var (groupNodes, flat, g1, g2) = BuildStableRoots();

        var events = 0;
        var resets = 0;
        groupNodes.CollectionChanged += (_, e) =>
        {
            events++;
            if (e.Action == NotifyCollectionChangedAction.Reset)
                resets++;
        };

        // «Все»: плоский узел скрыт, группы видны.
        flat.IsVisible = false;
        g1.IsVisible = true;
        g2.IsVisible = true;

        // Чистое переключение вкладки: состав GroupNodes НЕ меняется (тот же набор корней),
        // меняется только видимость — событий коллекции нет вовсе (нет Reset).
        groupNodes.ReplaceItemWise(new[] { flat, g1, g2 });

        // «Недавние»: плоский узел виден, группы скрыты.
        flat.IsVisible = true;
        g1.IsVisible = false;
        g2.IsVisible = false;

        // Повторное переключение обратно — снова без событий.
        groupNodes.ReplaceItemWise(new[] { flat, g1, g2 });

        Assert.Equal(0, events);
        Assert.Equal(0, resets);
    }

    /// <summary>
    /// При стабильном наборе корней объекты корней не пересоздаются между сменами вкладок:
    /// контейнеры TreeView не пересоздаются (источник роста heapAfterGcMb в issue #21).
    /// </summary>
    [Fact]
    public void ModeSwitch_StableComposition_KeepsRootInstances()
    {
        var (groupNodes, flat, g1, g2) = BuildStableRoots();

        // «Все» → «Недавние» → «Все»: объекты корней остаются теми же (не пересоздаются).
        groupNodes.ReplaceItemWise(new[] { flat, g1, g2 });
        flat.IsVisible = true;
        g1.IsVisible = false;
        g2.IsVisible = false;
        groupNodes.ReplaceItemWise(new[] { flat, g1, g2 });
        flat.IsVisible = false;
        g1.IsVisible = true;
        g2.IsVisible = true;

        Assert.Equal(3, groupNodes.Count);
        Assert.Same(flat, groupNodes[0]);
        Assert.Same(g1, groupNodes[1]);
        Assert.Same(g2, groupNodes[2]);
    }

    /// <summary>
    /// Старый подход (смена состава «Все»=[g1,g2] ↔ «Недавние»=[flat]) уходит в Reset из-за
    /// малого пересечения; стабильный набор корней Reset не даёт. Это ключевое отличие Этапа F.
    /// </summary>
    [Fact]
    public void AllToFlat_StableSet_NoReset_UnlikeCompositionChange()
    {
        var (groupNodes, flat, g1, g2) = BuildStableRoots();
        var resets = 0;
        groupNodes.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
                resets++;
        };

        // Старый подход: «Все» = [g1, g2], «Недавние» = [flat] — состав меняется целиком,
        // ReplaceItemWise уходит в Reset (малое пересечение составов).
        groupNodes.ReplaceItemWise(new[] { g1, g2 });
        groupNodes.ReplaceItemWise(new[] { flat });
        Assert.True(resets > 0, "Старый подход (смена состава) обязан давать Reset");

        // Новый подход: стабильный набор [_flatNode, g1, g2] — состав не меняется, Reset нет.
        resets = 0;
        groupNodes.ReplaceItemWise(new[] { flat, g1, g2 });
        groupNodes.ReplaceItemWise(new[] { flat, g1, g2 });
        Assert.Equal(0, resets);
    }

    /// <summary>В плоском режиме виден только плоский узел, группы скрыты.</summary>
    [Fact]
    public void IsVisible_FlatMode_OnlyFlatNodeVisible()
    {
        var (groupNodes, flat, g1, g2) = BuildStableRoots();

        flat.IsVisible = true;
        g1.IsVisible = false;
        g2.IsVisible = false;

        var visible = groupNodes.Where(n => n.IsVisible).ToList();
        Assert.Single(visible);
        Assert.Same(flat, visible[0]);
    }

    /// <summary>В режиме «Все» группы видны, плоский узел скрыт.</summary>
    [Fact]
    public void IsVisible_AllMode_GroupsVisibleFlatHidden()
    {
        var (groupNodes, flat, g1, g2) = BuildStableRoots();

        flat.IsVisible = false;
        g1.IsVisible = true;
        g2.IsVisible = true;

        var visible = groupNodes.Where(n => n.IsVisible).ToList();
        Assert.Equal(2, visible.Count);
        Assert.DoesNotContain(flat, visible);
    }

    /// <summary>Смена видимости поднимает PropertyChanged только при реальном изменении.</summary>
    [Fact]
    public void IsVisible_PropertyChanged_RaisedOnToggle()
    {
        var node = new GroupNodeViewModel(null);
        var changed = 0;
        node.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GroupNodeViewModel.IsVisible))
                changed++;
        };

        node.IsVisible = false;
        node.IsVisible = false; // повторная установка того же значения — без события
        node.IsVisible = true;

        Assert.Equal(2, changed);
    }

    /// <summary>
    /// RevealPlanner ищет цель только среди видимых корней: скрытые группы (issue #21,
    /// Этап F) остаются в GroupNodes, но не должны быть «домашним» узлом в плоском режиме.
    /// </summary>
    [Fact]
    public void RevealPlanner_SkipsHiddenRoots()
    {
        var ib = CreateBase("Цель");
        var flat = new GroupNodeViewModel(null);
        flat.Infobases.Add(ib);
        flat.PopulateItems();
        var hiddenGroup = CreateGroup("g1", "Скрытая", ib);

        // Плоский режим: группа скрыта, цель есть только в плоском узле.
        flat.IsVisible = true;
        hiddenGroup.IsVisible = false;
        var roots = new[] { flat, hiddenGroup };

        var leaf = RevealPlanner.PlanLeaf(roots, ib, revealToHome: true);

        Assert.Same(flat, leaf);
    }
}
