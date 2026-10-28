using System;
using System.Collections.Generic;
using System.IO;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты хранилища компоновки контекстного меню базы (issue #56): сохранение в файл
/// context_menu_layout.json (атомарная запись), чтение с нормализацией и устойчивость
/// к битому файлу.
/// </summary>
public sealed class ContextMenuLayoutStoreTests : IDisposable
{
    private readonly string _tempDir;

    public ContextMenuLayoutStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cm_context_menu_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Игнорируем: каталог мог быть занят или уже удалён.
        }
    }

    private ContextMenuLayoutStore CreateStore() => new(directoryOverride: _tempDir);

    [Fact]
    public void Load_WhenFileMissing_ReturnsNull()
    {
        var store = CreateStore();
        Assert.Null(store.Load());
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsLayout()
    {
        var store = CreateStore();
        var layout = ContextMenuLayouts.Default();
        store.Save(layout);

        Assert.True(File.Exists(store.FilePath));
        var restored = store.Load();
        Assert.NotNull(restored);
        Assert.Equal(layout.Root.Count, restored!.Root.Count);
        AssertItem(restored.Root[0], "LaunchEnterprise");
        // Разделители сохраняются как самостоятельные узлы (issue #374).
        Assert.Equal("separator", restored.Root[3].Kind);
        AssertItem(restored.Root[4], "CustomActions");
        // Группы — на своих местах.
        Assert.Equal("group", restored.Root[12].Kind);
        Assert.Equal(ContextMenuCatalog.GroupUpdateAndLink, restored.Root[12].GroupId);
        Assert.Equal(4, restored.Root[12].Children.Count);
        Assert.Equal(ContextMenuCatalog.GroupAdministration, restored.Root[13].GroupId);
        // «Публикация на веб-сервере» добавлена в «Администрирование» (issue #63),
        // «Развернуть тестовую копию» — п.10, 1.0.0.92 (тестовая копия одной кнопкой).
        Assert.Equal(12, restored.Root[13].Children.Count);
    }

    [Fact]
    public void Save_CreatesReadableJson_WithUtf8Cyrillic()
    {
        var store = CreateStore();
        var layout = new ContextMenuLayout
        {
            SchemaVersion = 1,
            Root = new List<ContextMenuNode>
            {
                ContextMenuLayouts.Item("LaunchEnterprise"),
                ContextMenuLayouts.Group(ContextMenuCatalog.GroupAdministration, new List<ContextMenuNode>
                {
                    ContextMenuLayouts.Item("SessionLock")
                })
            }
        };
        store.Save(layout);

        var text = File.ReadAllText(store.FilePath);
        // Кириллица записывается читаемыми символами, а не \uXXXX.
        Assert.DoesNotContain("\\u", text);
        // Ключи свойств в JSON — имена свойств модели.
        Assert.Contains("ItemId", text);
        Assert.Contains("Root", text);
    }

    [Fact]
    public void Load_BrokenFile_ReturnsNull_AndDoesNotThrow()
    {
        var store = CreateStore();
        File.WriteAllText(store.FilePath, "{ this is not valid json");

        var result = store.Load();
        Assert.Null(result);
        // Битый файл на диске не трогаем.
        Assert.True(File.Exists(store.FilePath));
    }

    [Fact]
    public void Save_LearvesNoTempFile_AtomicWrite()
    {
        var store = CreateStore();
        var layout = ContextMenuLayouts.Default();
        store.Save(layout);

        // Атомарная запись: временный файл после сохранения отсутствует.
        Assert.False(File.Exists(store.FilePath + ".tmp"));
    }

    [Fact]
    public void Load_FileWithUnknownItem_DropsUnknownNode()
    {
        var store = CreateStore();
        File.WriteAllText(store.FilePath,
            """
            {
              "SchemaVersion": 1,
              "Root": [
                { "Kind": "item", "ItemId": "LaunchEnterprise", "Visible": true },
                { "Kind": "item", "ItemId": "UnknownItemId", "Visible": true },
                { "Kind": "group", "GroupId": "Administration", "Visible": true, "Children": [] }
              ]
            }
            """);

        var result = store.Load();
        Assert.NotNull(result);
        Assert.Equal(2, result!.Root.Count);
        Assert.Equal("LaunchEnterprise", result.Root[0].ItemId);
        Assert.Equal("group", result.Root[1].Kind);
    }

    [Fact]
    public void Load_FileWithNullCollections_Normalizes()
    {
        var store = CreateStore();
        File.WriteAllText(store.FilePath,
            """
            {
              "SchemaVersion": 1,
              "Root": [
                { "Kind": "group", "GroupId": "Administration", "Children": null }
              ]
            }
            """);

        var result = store.Load();
        Assert.NotNull(result);
        Assert.Single(result!.Root);
        Assert.NotNull(result.Root[0].Children);
        Assert.Empty(result.Root[0].Children!);
    }

    [Fact]
    public void Load_FileWithSeparatorNodes_KeepsThem()
    {
        var store = CreateStore();
        File.WriteAllText(store.FilePath,
            """
            {
              "SchemaVersion": 1,
              "Root": [
                { "Kind": "item", "ItemId": "LaunchEnterprise", "Visible": true },
                { "Kind": "separator", "Visible": true },
                { "Kind": "item", "ItemId": "LaunchConfigurator", "Visible": true }
              ]
            }
            """);

        var result = store.Load();
        Assert.NotNull(result);
        Assert.Equal(3, result!.Root.Count);
        Assert.Equal("separator", result.Root[1].Kind);
        Assert.Equal("LaunchConfigurator", result.Root[2].ItemId);
    }

    [Fact]
    public void Load_FileWithSeparatorBefore_MigratesToSeparatorNode()
    {
        var store = CreateStore();
        File.WriteAllText(store.FilePath,
            """
            {
              "SchemaVersion": 1,
              "Root": [
                { "Kind": "item", "ItemId": "LaunchEnterprise", "Visible": true },
                { "Kind": "item", "ItemId": "LaunchConfigurator", "Visible": true, "SeparatorBefore": true }
              ]
            }
            """);

        var result = store.Load();
        Assert.NotNull(result);
        Assert.Equal(3, result!.Root.Count);
        Assert.Equal("separator", result.Root[1].Kind);
        Assert.Equal("LaunchConfigurator", result.Root[2].ItemId);
        Assert.False(result.Root[2].SeparatorBefore);
    }

    [Fact]
    public void Save_OverwritesExistingFile()
    {
        var store = CreateStore();
        var first = new ContextMenuLayout();
        first.Root.Add(ContextMenuLayouts.Item("LaunchEnterprise"));
        store.Save(first);

        var second = new ContextMenuLayout();
        second.Root.Add(ContextMenuLayouts.Item("EditSettings"));
        store.Save(second);

        var restored = store.Load();
        Assert.NotNull(restored);
        Assert.Single(restored!.Root);
        Assert.Equal("EditSettings", restored.Root[0].ItemId);
    }

    private static void AssertItem(ContextMenuNode node, string itemId)
    {
        Assert.NotNull(node);
        Assert.Equal("item", node.Kind);
        Assert.Equal(itemId, node.ItemId);
    }
}