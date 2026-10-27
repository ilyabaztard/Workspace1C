using System.Collections.Generic;
using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты ViewModel редактора структуры контекстного меню базы (issue #56): создание
/// дерева из компоновки, перемещение (вверх/вниз/в группу/в корень), скрытие пунктов,
/// добавление/правка/удаление групп, разделители, сброс к стандартной компоновке,
/// сохранение (layout эквивалентен дереву) и фиксация видимости «Новинок программы».
/// Чистый .NET — без UI.
/// </summary>
public sealed class ContextMenuEditorViewModelTests
{
    private static ContextMenuEditorViewModel CreateViewModel() => new(null);

    // ---- Построение дерева ----

    [Fact]
    public void Constructor_BuildsTreeFromDefaultLayout()
    {
        var vm = CreateViewModel();
        // 20 пунктов/групп + 5 разделителей.
        Assert.Equal(25, vm.Roots.Count);
        Assert.Equal("LaunchEnterprise", vm.Roots[0].Node.ItemId);
        Assert.Equal("separator", vm.Roots[3].Node.Kind);
        // Группы собраны в список для «Поместить в группу».
        Assert.Contains(vm.Groups, g => g.Node.GroupId == ContextMenuCatalog.GroupAdministration);
    }

    // ---- Создание группы ----

    [Fact]
    public void AddGroup_CreatesGroupWithTitleInRoot()
    {
        var vm = CreateViewModel();
        var before = vm.Roots.Count;

        vm.AddGroupCommand.Execute(new ContextMenuGroupEditParams("Моя группа"));

        Assert.Equal(before + 1, vm.Roots.Count);
        var group = vm.Roots[^1];
        Assert.Equal("group", group.Node.Kind);
        Assert.Equal("Моя группа", group.Node.CustomTitle);
        Assert.Equal("Моя группа", group.Title);
        Assert.True(vm.Groups.Contains(group));
        Assert.True(ReferenceEquals(vm.SelectedItem, group));
    }

    [Fact]
    public void AddGroup_WithEmptyTitle_UsesDefaultName()
    {
        var vm = CreateViewModel();
        var before = vm.Roots.Count;

        vm.AddGroupCommand.Execute(new ContextMenuGroupEditParams("   "));

        Assert.Equal(before + 1, vm.Roots.Count);
        Assert.False(string.IsNullOrWhiteSpace(vm.Roots[^1].Node.CustomTitle));
    }

    [Fact]
    public void AddGroup_InsideSelectedGroup_AddsChild()
    {
        var vm = CreateViewModel();
        // Выбираем первую группу дерева (стандартная компоновка содержит группы).
        var parent = vm.Groups[0];
        vm.SelectedItem = parent;
        var before = parent.Children.Count;

        vm.AddGroupCommand.Execute(new ContextMenuGroupEditParams("Вложенная"));

        Assert.Equal(before + 1, parent.Children.Count);
        var child = parent.Children[^1];
        Assert.Equal("Вложенная", child.Node.CustomTitle);
        Assert.True(ReferenceEquals(child.Parent, parent));
    }

    // ---- Переименование ----

    [Fact]
    public void EditGroup_RenamesSelectedGroup()
    {
        var vm = CreateViewModel();
        var group = vm.Groups[0];
        vm.SelectedItem = group;

        vm.EditGroupCommand.Execute(new ContextMenuGroupEditParams("Новое имя"));

        Assert.Equal("Новое имя", group.Node.CustomTitle);
        Assert.Equal("Новое имя", group.Title);
    }

    [Fact]
    public void EditGroup_WithoutSelection_DoesNothing()
    {
        var vm = CreateViewModel();
        vm.SelectedItem = null;

        vm.EditGroupCommand.Execute(new ContextMenuGroupEditParams("Имя"));

        // Ничего не сломалось, дерево не изменилось.
        Assert.True(vm.Roots.Count > 0);
    }

    // ---- Удаление ----

    [Fact]
    public void DeleteGroup_EmptyGroup_RemovesIt()
    {
        var vm = CreateViewModel();
        vm.AddGroupCommand.Execute(new ContextMenuGroupEditParams("Пустая"));
        var group = vm.SelectedItem!;
        var before = vm.Roots.Count;

        vm.DeleteGroupCommand.Execute(false);

        Assert.Equal(before - 1, vm.Roots.Count);
        Assert.DoesNotContain(group, vm.Groups);
    }

    [Fact]
    public void DeleteGroup_WithItems_MovesChildrenToRoot()
    {
        var vm = CreateViewModel();
        var group = vm.Groups.First(g => g.Children.Count > 0);
        vm.SelectedItem = group;
        var children = group.Children.ToList();
        var beforeRoot = vm.Roots.Count;

        vm.DeleteGroupCommand.Execute(true);

        // Дети перенесены в корень на место группы.
        Assert.Equal(beforeRoot + children.Count - 1, vm.Roots.Count);
        foreach (var child in children)
            Assert.True(ReferenceEquals(child.Parent, null));
    }

    // ---- Перемещение ----

    [Fact]
    public void MoveUp_MovesSelectedItemUp()
    {
        var vm = CreateViewModel();
        var target = vm.Roots[1];
        vm.SelectedItem = target;

        vm.MoveUpCommand.Execute(null);

        Assert.Equal(0, vm.Roots.IndexOf(target));
    }

    [Fact]
    public void MoveDown_MovesSelectedItemDown()
    {
        var vm = CreateViewModel();
        var target = vm.Roots[0];
        vm.SelectedItem = target;

        vm.MoveDownCommand.Execute(null);

        Assert.Equal(1, vm.Roots.IndexOf(target));
    }

    [Fact]
    public void MoveToGroup_MovesSelectedItemIntoGroup()
    {
        var vm = CreateViewModel();
        var group = vm.Groups[0];
        var item = vm.Roots.First(r => r.Node.Kind == "item");
        vm.SelectedItem = item;
        vm.SelectedGroup = group;

        vm.MoveToGroupCommand.Execute(null);

        Assert.Contains(item, group.Children);
        Assert.True(ReferenceEquals(item.Parent, group));
    }

    [Fact]
    public void MoveToRoot_MovesSelectedItemFromGroupToRoot()
    {
        var vm = CreateViewModel();
        var adminGroup = vm.Groups.First(g => g.Node.GroupId == ContextMenuCatalog.GroupAdministration);
        var child = adminGroup.Children[0];
        vm.SelectedItem = child;

        vm.MoveToRootCommand.Execute(null);

        Assert.DoesNotContain(child, adminGroup.Children);
        Assert.True(ReferenceEquals(child.Parent, null));
        Assert.Contains(child, vm.Roots);
    }

    // ---- Скрытие ----

    [Fact]
    public void ToggleVisibility_HidesItem()
    {
        var vm = CreateViewModel();
        var item = vm.Roots[0];

        Assert.True(item.IsVisible);
        item.IsVisible = false;

        Assert.False(item.Node.Visible);
    }

    // ---- Разделители ----

    [Fact]
    public void AddSeparator_InsertsAfterSelectedItem()
    {
        var vm = CreateViewModel();
        var selected = vm.Roots[0];
        vm.SelectedItem = selected;
        var before = vm.Roots.Count;

        vm.AddSeparatorCommand.Execute(null);

        Assert.Equal(before + 1, vm.Roots.Count);
        Assert.Equal("separator", vm.Roots[1].Node.Kind);
    }

    [Fact]
    public void DeleteSeparator_RemovesItWithoutConfirmation()
    {
        var vm = CreateViewModel();
        var separator = vm.Roots.First(r => r.Node.Kind == "separator");
        vm.SelectedItem = separator;
        var before = vm.Roots.Count;

        vm.DeleteGroupCommand.Execute(false);

        Assert.Equal(before - 1, vm.Roots.Count);
    }

    // ---- Сброс ----

    [Fact]
    public void ResetToDefault_RestoresStandardLayout()
    {
        var vm = CreateViewModel();
        vm.AddGroupCommand.Execute(new ContextMenuGroupEditParams("Лишняя группа"));
        Assert.Equal(26, vm.Roots.Count);

        vm.ResetDefaultCommand.Execute(null);

        Assert.Equal(25, vm.Roots.Count);
        Assert.Equal("LaunchEnterprise", vm.Roots[0].Node.ItemId);
    }

    // ---- Сохранение ----

    [Fact]
    public void Save_ProducesLayoutEquivalentToTree()
    {
        var store = new MemoryLayoutStore();
        var vm = new ContextMenuEditorViewModel(store);
        // Перенесём «Пин» в корень после «Запустить» (уже в корне) — просто сдвинем вверх.
        var pin = vm.Roots.First(r => r.Node.ItemId == "Pin");
        vm.SelectedItem = pin;
        vm.MoveUpCommand.Execute(null);

        vm.SaveCommand.Execute(null);

        var saved = store.SavedLayout;
        Assert.NotNull(saved);
        Assert.Equal(vm.Roots.Count, saved!.Root.Count);
        // Порядок сохраняется: «Пин» сдвинут вверх (был индекс 7, стал 6 — перед «Избранное»).
        var pinIndex = saved.Root.FindIndex(n => n.ItemId == "Pin");
        Assert.Equal(6, pinIndex);
        var beforePinIndex = saved.Root.FindIndex(n => n.ItemId == "ToFavorites");
        Assert.True(beforePinIndex > pinIndex);
    }

    [Fact]
    public void Load_RestoresLayoutFromStore()
    {
        var custom = new ContextMenuLayout
        {
            Root = new List<ContextMenuNode>
            {
                ContextMenuLayouts.Item("LaunchEnterprise"),
                ContextMenuLayouts.Item("EditSettings")
            }
        };
        var store = new MemoryLayoutStore(custom);
        var vm = new ContextMenuEditorViewModel(store);

        // Конструктор загрузил компоновку из хранилища и дополнил её Essential-пунктами
        // («Пользовательские действия», «Для выделенных») и группой «Новинки программы».
        Assert.Equal(5, vm.Roots.Count);
        Assert.Equal("LaunchEnterprise", vm.Roots[0].Node.ItemId);
        Assert.Equal("EditSettings", vm.Roots[1].Node.ItemId);
        Assert.Equal(ContextMenuCatalog.GroupNewItems, vm.Roots[^1].Node.GroupId);
    }

    // ---- «Новинки программы» (issue #4) ----

    [Fact]
    public void NewItemsGroup_CanToggleVisibility_IsFalse()
    {
        // Компоновка без пункта-«новинки»: редактор создаёт группу «Новинки программы»,
        // видимость которой зафиксирована (менять можно только местоположение).
        var custom = new ContextMenuLayout
        {
            Root = new List<ContextMenuNode> { ContextMenuLayouts.Item("LaunchEnterprise") }
        };
        var store = new MemoryLayoutStore(custom);
        var vm = new ContextMenuEditorViewModel(store);

        var newsGroup = vm.Groups.FirstOrDefault(g =>
            g.Node.GroupId == ContextMenuCatalog.GroupNewItems);
        Assert.NotNull(newsGroup);
        Assert.False(newsGroup!.CanToggleVisibility);
        Assert.Contains(newsGroup.Children, c => c.Node.ItemId == "CreateDesktopShortcut");
    }

    /// <summary>Хранилище-заглушка для тестов (без файла на диске).</summary>
    private sealed class MemoryLayoutStore : IContextMenuLayoutStore
    {
        private readonly ContextMenuLayout? _initial;

        public MemoryLayoutStore(ContextMenuLayout? initial = null)
        {
            _initial = initial;
        }

        public ContextMenuLayout? SavedLayout { get; private set; }

        public string FilePath => "memory://context_menu_layout.json";

        public ContextMenuLayout? Load() => _initial;

        public void Save(ContextMenuLayout layout) => SavedLayout = layout;
    }
}