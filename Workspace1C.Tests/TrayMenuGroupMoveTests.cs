using System;
using System.IO;
using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #62 «Правка меню трея» (замечание 7OH: «Создал группу, но переместить туда
/// 2 блока не смог»): перемещение блоков/разделителей в пользовательскую группу
/// (подменю) и обратно в корень.
/// </summary>
public sealed class TrayMenuGroupMoveTests
{
    private const string GroupTitle = "Моя группа";

    private static TrayMenuEditorViewModel BuildEditorWithGroup()
    {
        var vm = new TrayMenuEditorViewModel(store: null);
        vm.AddGroupCommand.Execute(new TrayMenuGroupEditParams(GroupTitle, "Folder"));
        return vm;
    }

    [Fact]
    public void MoveSelectedToGroup_MovesBlockIntoGroupChildren()
    {
        var vm = BuildEditorWithGroup();
        var group = vm.SelectedItem!;

        var favorites = vm.Roots.First(i => i.Node.BlockKind == TrayMenuBlockKind.Favorites);
        vm.SelectedItem = favorites;
        Assert.True(vm.CanMoveToGroup);

        Assert.True(vm.MoveSelectedToGroup(group));

        // Блок «Избранные» теперь в детях группы, из корня исчез.
        Assert.Contains(group.Children, c => c.Node.BlockKind == TrayMenuBlockKind.Favorites);
        Assert.DoesNotContain(vm.Roots, i => i.Node.BlockKind == TrayMenuBlockKind.Favorites);
    }

    [Fact]
    public void MoveSelectedToGroup_GroupItself_NotAllowed()
    {
        var vm = BuildEditorWithGroup();
        var first = vm.SelectedItem!;
        // Вторая группа.
        vm.AddGroupCommand.Execute(new TrayMenuGroupEditParams("Вторая", "Star"));
        var second = vm.SelectedItem!;

        // Попытка вложить первую группу во вторую отклоняется (одна вложенность).
        vm.SelectedItem = first;
        Assert.False(vm.CanMoveToGroup);
        Assert.False(vm.MoveSelectedToGroup(second));
        Assert.DoesNotContain(second.Children, c => ReferenceEquals(c, first));
    }

    [Fact]
    public void UnGroupSelected_ReturnsBlockToRoot_AfterGroup()
    {
        var vm = BuildEditorWithGroup();
        var group = vm.SelectedItem!;

        var favorites = vm.Roots.First(i => i.Node.BlockKind == TrayMenuBlockKind.Favorites);
        vm.SelectedItem = favorites;
        vm.MoveSelectedToGroup(group);
        Assert.True(vm.CanUnGroup);

        // Выбираем перемещённый узел из детей группы.
        vm.SelectedItem = group.Children.First(c => c.Node.BlockKind == TrayMenuBlockKind.Favorites);
        Assert.True(vm.UnGroupSelected());

        Assert.True(vm.CanUnGroup == false);
        Assert.Contains(vm.Roots, i => i.Node.BlockKind == TrayMenuBlockKind.Favorites);
    }

    [Fact]
    public void MoveSelectedToGroup_Separator_Allowed()
    {
        var vm = BuildEditorWithGroup();
        vm.AddSeparatorCommand.Execute(null);
        var separator = vm.SelectedItem!;
        var group = vm.UserGroups.First();

        vm.MoveSelectedToGroup(group);

        Assert.Contains(group.Children, c => c.IsSeparator);
    }

    [Fact]
    public void Resolver_BuildEffective_FiltersHiddenChildren()
    {
        var layout = new TrayMenuLayout { SchemaVersion = 1 };
        var group = new TrayMenuNode
        {
            Kind = "block",
            BlockKind = TrayMenuBlockKind.UserGroup,
            GroupId = "g1",
            CustomTitle = "Группа",
            Style = TrayMenuBlockStyle.Submenu
        };
        group.Children.Add(new TrayMenuNode
        {
            Kind = "block", BlockKind = TrayMenuBlockKind.Favorites, Visible = true
        });
        group.Children.Add(new TrayMenuNode
        {
            Kind = "block", BlockKind = TrayMenuBlockKind.Recent, Visible = false
        });
        layout.Root.Add(group);

        var effective = TrayMenuResolver.BuildEffective(layout);

        var effectiveGroup = Assert.Single(effective.Root);
        Assert.Single(effectiveGroup.Children);
        Assert.Equal(TrayMenuBlockKind.Favorites, effectiveGroup.Children[0].BlockKind);
    }

    [Fact]
    public void Builder_GroupSubmenu_IncludesChildren()
    {
        var layout = new TrayMenuLayout { SchemaVersion = 1 };
        var group = new TrayMenuNode
        {
            Kind = "block",
            BlockKind = TrayMenuBlockKind.UserGroup,
            GroupId = "g1",
            CustomTitle = "Группа",
            Style = TrayMenuBlockStyle.Submenu
        };
        group.Children.Add(new TrayMenuNode
        {
            Kind = "block", BlockKind = TrayMenuBlockKind.Favorites, Visible = true
        });
        layout.Root.Add(group);

        var items = new TrayMenuBuilder(_ => "T").Build(layout);

        var submenu = Assert.Single(items.Where(i => i.Kind == TrayItemKind.Submenu));
        Assert.NotEmpty(submenu.Children);
    }

    // ---------- Разметка редакторов (WPF/Avalonia) ----------

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
    public void WpfEditor_HasMoveToGroupAndUnGroupButtons()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "TrayMenuEditorWindow.xaml"));
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "TrayMenuEditorWindow.xaml.cs"));

        Assert.Contains("OnMoveToGroupClick", xaml, StringComparison.Ordinal);
        Assert.Contains("OnUnGroupClick", xaml, StringComparison.Ordinal);
        Assert.Contains("MoveSelectedToGroup", code, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaEditorAndPicker_SupportGroups()
    {
        var editor = ReadProjectFile(Path.Combine("Workspace1C", "Views", "TrayMenuEditorWindow.Avalonia.cs"));
        var picker = ReadProjectFile(Path.Combine("Workspace1C", "Views", "TrayMenuGroupPickWindow.Avalonia.cs"));

        Assert.Contains("TrayMenuGroupPickWindow", editor, StringComparison.Ordinal);
        Assert.Contains("MoveSelectedToGroup", editor, StringComparison.Ordinal);
        Assert.Contains("SelectedGroup", picker, StringComparison.Ordinal);
    }
}