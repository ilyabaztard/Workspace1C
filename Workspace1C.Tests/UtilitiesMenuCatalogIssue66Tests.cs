using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты issue #66 «Лишний пункт в Утилиты»: пункт «Блокировка сеансов ИБ»
/// (SessionLock) полностью удалён из каталога меню «Утилиты» и из стандартной
/// компоновки; старые пользовательские компоновки (utilities_menu_layout.json)
/// с ItemId "SessionLock" не роняют загрузку редактора — узел отбрасывается,
/// остальные пункты сохраняются. Функция блокировки (hotkey Ctrl+Alt+L,
/// контекстное меню базы, окно SessionLockWindow) не затрагивается.
/// </summary>
public sealed class UtilitiesMenuCatalogIssue66Tests
{
    [Fact]
    public void UtilitiesMenuCatalog_DefaultLayout_DoesNotContainSessionLockItem()
    {
        var layout = UtilitiesMenuLayouts.Default();

        var ids = new HashSet<string>(StringComparer.Ordinal);
        UtilitiesMenuResolver.CollectItemIds(layout.Root, ids);

        Assert.DoesNotContain("SessionLock", ids);
    }

    [Fact]
    public void UtilitiesMenuCatalog_Find_SessionLock_ReturnsNull()
    {
        // Пункт полностью удалён из каталога: никакой построитель меню
        // (WPF/Avalonia) не создаст его даже в пользовательской компоновке.
        Assert.Null(UtilitiesMenuCatalog.Find("SessionLock"));
    }

    [Fact]
    public void EditorViewModel_LoadWithUnknownItemId_SessionLock_DropsNode_KeepsOthers()
    {
        // Старый файл компоновки, сохранённый до удаления пункта: узел с
        // ItemId "SessionLock" неизвестен каталогу → отбрасывается при загрузке,
        // остальные пункты корня сохраняются (загрузка не падает).
        var dir = Path.Combine(Path.GetTempPath(), "issue66_tests_" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new UtilitiesMenuLayoutStore(null, dir);
            store.Save(new UtilitiesMenuLayout
            {
                Root = new System.Collections.Generic.List<UtilitiesMenuNode>
                {
                    UtilitiesMenuLayouts.Item("CommandPalette"),
                    // Старая компоновка: пункт «Блокировка сеансов ИБ» в меню «Утилиты».
                    new UtilitiesMenuNode { Kind = "item", ItemId = "SessionLock" },
                    UtilitiesMenuLayouts.Item("ActualReleases"),
                }
            });

            var vm = new UtilitiesMenuEditorViewModel(new UtilitiesMenuLayoutStore(null, dir));

            var ids = CollectItemIds(vm.Roots).ToList();
            Assert.DoesNotContain("SessionLock", ids);
            Assert.Contains("CommandPalette", ids);
            Assert.Contains("ActualReleases", ids);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void EditorViewModel_Reset_DefaultLayout_HasNoSessionLock()
    {
        // Защита от регресса: сброс к стандартной компоновке не возвращает
        // удалённый пункт в структуру меню «Утилиты».
        var vm = new UtilitiesMenuEditorViewModel(null);
        vm.ResetDefaultCommand.Execute(null);

        var ids = CollectItemIds(vm.Roots).ToList();
        Assert.DoesNotContain("SessionLock", ids);
    }

    /// <summary>Собирает все ItemId узлов дерева редактора (рекурсивно).</summary>
    private static IEnumerable<string> CollectItemIds(IEnumerable<UtilitiesMenuNodeItem> nodes)
    {
        foreach (var node in nodes)
        {
            if (string.Equals(node.Node.Kind, "item", StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(node.Node.ItemId))
            {
                yield return node.Node.ItemId!;
            }
            foreach (var child in CollectItemIds(node.Children))
                yield return child;
        }
    }
}