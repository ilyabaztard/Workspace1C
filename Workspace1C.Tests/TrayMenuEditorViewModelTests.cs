using System;
using System.IO;
using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты ViewModel редактора структуры меню трея (issue #62): дерево блоков
/// верхнего уровня, защита стандартных блоков от удаления, смена стиля блока,
/// создание пользовательских групп с иконками, перемещение, видимость
/// и сохранение/загрузка. Чистый .NET — без UI.
/// </summary>
public sealed class TrayMenuEditorViewModelTests
{
    private static TrayMenuEditorViewModel CreateViewModel() => new(null);

    [Fact]
    public void DefaultTree_HasThreeBlocks_NotDeletable_Buildable()
    {
        var vm = CreateViewModel();

        Assert.Equal(3, vm.Roots.Count);
        var kinds = vm.Roots.Select(r => r.Node.BlockKind!.Value).ToList();
        Assert.Contains(TrayMenuBlockKind.Favorites, kinds);
        Assert.Contains(TrayMenuBlockKind.Recent, kinds);
        Assert.Contains(TrayMenuBlockKind.Services, kinds);

        // Стандартные блоки нельзя удалить (и продублировать — они единственны).
        foreach (var root in vm.Roots)
        {
            vm.SelectedItem = root;
            Assert.False(vm.CanDelete);
        }

        // Компоновка пригодна для построения: BuildEffective не роняет и не теряет блоки.
        vm.SelectedItem = null;
        var effective = TrayMenuResolver.BuildEffective(vm.Layout);
        Assert.Equal(3, effective.Root.Count);
    }

    [Fact]
    public void ChangeStyle_BlockToSubmenu_AndBack_Persisted()
    {
        using var tmp = TempDir();
        var store = new TrayMenuLayoutStore(directoryOverride: tmp.Path);
        var vm = new TrayMenuEditorViewModel(store);
        var fav = vm.Roots.First(r => r.Node.BlockKind == TrayMenuBlockKind.Favorites);

        vm.SelectedItem = fav;
        Assert.Equal(TrayMenuBlockStyle.Block, vm.SelectedBlockStyle);
        vm.SelectedBlockStyle = TrayMenuBlockStyle.Submenu;
        Assert.Equal(TrayMenuBlockStyle.Submenu, fav.Node.Style);
        Assert.Equal(TrayMenuBlockStyle.Submenu, vm.SelectedBlockStyle);
        vm.SelectedBlockStyle = TrayMenuBlockStyle.Block;
        Assert.Equal(TrayMenuBlockStyle.Block, fav.Node.Style);
        vm.SaveCommand.Execute(null);

        // Грузим из файла: стиль «Блок» сохранён.
        var reloaded = new TrayMenuEditorViewModel(store);
        var favReloaded = reloaded.Roots.First(r => r.Node.BlockKind == TrayMenuBlockKind.Favorites);
        Assert.Equal(TrayMenuBlockStyle.Block, favReloaded.Node.Style);

        // Меняем снова и переживаем сохранение/загрузку.
        reloaded.SelectedItem = favReloaded;
        reloaded.SelectedBlockStyle = TrayMenuBlockStyle.Submenu;
        reloaded.SaveCommand.Execute(null);
        var reloaded2 = new TrayMenuEditorViewModel(store);
        var favReloaded2 = reloaded2.Roots.First(r => r.Node.BlockKind == TrayMenuBlockKind.Favorites);
        Assert.Equal(TrayMenuBlockStyle.Submenu, favReloaded2.Node.Style);
    }

    [Fact]
    public void AddUserGroup_WithIcon_SaveLoad_And_MoveUpDown_AndVisibleToggle()
    {
        using var tmp = TempDir();
        var store = new TrayMenuLayoutStore(directoryOverride: tmp.Path);
        var vm = new TrayMenuEditorViewModel(store);
        var icon = vm.CatalogIcons.FirstOrDefault();

        vm.AddGroupCommand.Execute(new TrayMenuGroupEditParams("Моя группа", icon?.Key));

        var group = vm.SelectedItem!;
        Assert.Equal(TrayMenuBlockKind.UserGroup, group.Node.BlockKind);
        Assert.Equal("Моя группа", group.Node.CustomTitle);
        Assert.False(string.IsNullOrWhiteSpace(group.Node.GroupId));
        if (icon is not null)
        {
            Assert.Equal(icon.Key, group.Node.IconKey);
            Assert.Equal(icon.Color, group.Node.IconColor);
            Assert.Null(group.Node.ExternalIconPath);
        }

        // Перемещение вверх/вниз.
        var index = vm.Roots.IndexOf(group);
        Assert.True(vm.CanMoveSelectedUp);
        vm.MoveUpCommand.Execute(null);
        Assert.Equal(index - 1, vm.Roots.IndexOf(group));
        Assert.True(vm.CanMoveSelectedDown);
        vm.MoveDownCommand.Execute(null);
        Assert.Equal(index, vm.Roots.IndexOf(group));

        // Переключение видимости.
        Assert.True(group.IsVisible);
        group.IsVisible = false;
        Assert.False(group.Node.Visible);

        // Сохранение и перезагрузка: группа с именем, иконкой, скрытая, в новом месте.
        vm.SaveCommand.Execute(null);
        var reloaded = new TrayMenuEditorViewModel(store);
        var reloadedGroup = reloaded.Roots.First(r => r.Node.BlockKind == TrayMenuBlockKind.UserGroup);
        Assert.Equal("Моя группа", reloadedGroup.Node.CustomTitle);
        Assert.False(reloadedGroup.Node.Visible);
        if (icon is not null)
            Assert.Equal(icon.Key, reloadedGroup.Node.IconKey);
    }

    [Fact]
    public void AddSeparator_ThenDelete_RemovesIt_AndUserGroupDeletable()
    {
        var vm = CreateViewModel();

        vm.AddSeparatorCommand.Execute(null);
        var sep = vm.SelectedItem!;
        Assert.Equal("separator", sep.Node.Kind);
        Assert.True(vm.CanDelete);
        vm.DeleteCommand.Execute(null);
        Assert.Equal(3, vm.Roots.Count);

        // Пользовательскую группу удалить можно.
        vm.AddGroupCommand.Execute(new TrayMenuGroupEditParams("Группа", null));
        var group = vm.SelectedItem!;
        Assert.True(vm.CanDelete);
        vm.DeleteCommand.Execute(null);
        Assert.Equal(3, vm.Roots.Count);
    }

    [Fact]
    public void ResetToDefault_RestoresThreeBlocks()
    {
        var vm = CreateViewModel();
        vm.AddGroupCommand.Execute(new TrayMenuGroupEditParams("Лишняя группа", null));
        Assert.Equal(4, vm.Roots.Count);

        vm.ResetDefaultCommand.Execute(null);

        Assert.Equal(3, vm.Roots.Count);
        Assert.All(vm.Roots, r => Assert.Equal("block", r.Node.Kind));
    }

    [Fact]
    public void EditGroup_RenamesSelectedUserGroup()
    {
        var vm = CreateViewModel();
        vm.AddGroupCommand.Execute(new TrayMenuGroupEditParams("Старое имя", null));
        var group = vm.SelectedItem!;

        vm.EditGroupCommand.Execute(new TrayMenuGroupEditParams("Новое имя", null));

        Assert.Equal("Новое имя", group.Node.CustomTitle);
    }

    /// <summary>Временный каталог, удаляемый при освобождении.</summary>
    private static TempDirectory TempDir() => new();

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cm_tray_menu_vm_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Игнорируем: каталог мог быть занят или уже удалён.
            }
        }
    }
}
