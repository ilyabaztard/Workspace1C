using System;
using System.IO;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты хранилища компоновки меню трея (issue #62): сохранение в файл
/// tray_menu_layout.json (атомарная запись), чтение с нормализацией и
/// устойчивость к битому файлу.
/// </summary>
public sealed class TrayMenuLayoutStoreTests : IDisposable
{
    private readonly string _tempDir;

    public TrayMenuLayoutStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cm_tray_menu_store_" + Guid.NewGuid().ToString("N"));
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

    private TrayMenuLayoutStore CreateStore() => new(directoryOverride: _tempDir);

    [Fact]
    public void Load_WhenFileMissing_ReturnsNull()
    {
        var store = CreateStore();

        Assert.Null(store.Load());
    }

    [Fact]
    public void SaveLoad_Roundtrip_PreservesStyleAndVisibility()
    {
        var store = CreateStore();
        var layout = new TrayMenuLayout
        {
            Root =
            {
                new TrayMenuNode { Kind = "block", BlockKind = TrayMenuBlockKind.Favorites, Style = TrayMenuBlockStyle.Submenu },
                new TrayMenuNode { Kind = "block", BlockKind = TrayMenuBlockKind.Recent, Visible = false },
                new TrayMenuNode { Kind = "block", BlockKind = TrayMenuBlockKind.Services },
                TrayMenuLayouts.Separator(),
            },
        };

        store.Save(layout);
        var loaded = CreateStore().Load();

        Assert.NotNull(loaded);
        Assert.Equal(4, loaded!.Root.Count);
        Assert.Equal(TrayMenuBlockStyle.Submenu, loaded.Root[0].Style);
        Assert.False(loaded.Root[1].Visible);
        Assert.Equal(TrayMenuBlockStyle.Submenu, loaded.Root[2].Style);
        Assert.Equal("separator", loaded.Root[3].Kind);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsNull_AndDoesNotTouchFile()
    {
        var store = CreateStore();
        File.WriteAllText(store.FilePath, "{ not valid json ");

        var loaded = store.Load();

        Assert.Null(loaded);
        // Файл на диске не удаляется и не перезаписывается (пользователь сможет его поправить).
        Assert.True(File.Exists(store.FilePath));
    }

    [Fact]
    public void Load_NormalizesHandWrittenFile_DropsUnknownKind_DefaultsStyle()
    {
        var store = CreateStore();
        File.WriteAllText(store.FilePath, """
        {
          "SchemaVersion": 1,
          "Root": [
            { "Kind": "block", "BlockKind": "Favorites" },
            { "Kind": "block", "BlockKind": "Services" },
            { "Kind": "unknown" }
          ]
        }
        """);

        var loaded = store.Load();

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.Root.Count);
        Assert.Equal(TrayMenuBlockStyle.Block, loaded.Root[0].Style);
        Assert.True(loaded.Root[0].Visible);
        Assert.Equal(TrayMenuBlockStyle.Submenu, loaded.Root[1].Style);
    }
}
