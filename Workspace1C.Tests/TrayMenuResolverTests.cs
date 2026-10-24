using System;
using System.IO;
using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты резолвера компоновки меню трея (issue #62): выбор компоновки по настройкам,
/// нормализация и подготовка к построению (видимость).
/// </summary>
public sealed class TrayMenuResolverTests
{
    [Fact]
    public void Resolve_Off_ReturnsDefault()
    {
        var settings = new AppSettings { TrayMenuEditingEnabled = false };

        var layout = TrayMenuResolver.Resolve(settings, store: null);

        Assert.Equal(3, layout.Root.Count);
        var kinds = layout.Root.Select(n => n.BlockKind!.Value).ToList();
        Assert.Contains(TrayMenuBlockKind.Favorites, kinds);
        Assert.Contains(TrayMenuBlockKind.Recent, kinds);
        Assert.Contains(TrayMenuBlockKind.Services, kinds);
    }

    [Fact]
    public void Resolve_On_NoFile_ReturnsDefault()
    {
        using var tmp = TempDir();
        var settings = new AppSettings { TrayMenuEditingEnabled = true };
        var store = new TrayMenuLayoutStore(directoryOverride: tmp.Path);

        var layout = TrayMenuResolver.Resolve(settings, store);

        Assert.Equal(3, layout.Root.Count);
        Assert.All(layout.Root, n => Assert.Equal("block", n.Kind));
    }

    [Fact]
    public void Resolve_On_WithFile_ReturnsCustom()
    {
        using var tmp = TempDir();
        var store = new TrayMenuLayoutStore(directoryOverride: tmp.Path);
        store.Save(new TrayMenuLayout
        {
            Root = { TrayMenuLayouts.Block(TrayMenuBlockKind.Favorites) },
        });
        var settings = new AppSettings { TrayMenuEditingEnabled = true };

        var layout = TrayMenuResolver.Resolve(settings, store);

        Assert.Single(layout.Root);
        Assert.Equal(TrayMenuBlockKind.Favorites, layout.Root[0].BlockKind);
    }

    [Fact]
    public void NormalizeLayout_DropsUnknownKind_DefaultsStyle()
    {
        var layout = new TrayMenuLayout
        {
            Root =
            {
                new TrayMenuNode { Kind = "block", BlockKind = TrayMenuBlockKind.Favorites },
                new TrayMenuNode { Kind = "block", BlockKind = TrayMenuBlockKind.Services },
                new TrayMenuNode { Kind = "bogus", CustomTitle = "?" },
                new TrayMenuNode { Kind = "block", BlockKind = (TrayMenuBlockKind)999 },
                TrayMenuLayouts.Separator(),
            },
        };

        var result = TrayMenuResolver.NormalizeLayout(layout);

        // Неизвестные Kind и BlockKind отброшены; разделитель сохранён.
        Assert.Equal(3, result.Root.Count);
        Assert.Equal(TrayMenuBlockStyle.Block, result.Root[0].Style);
        Assert.True(result.Root[0].Visible);
        Assert.Equal(TrayMenuBlockStyle.Submenu, result.Root[1].Style);
        Assert.Equal("separator", result.Root[2].Kind);
    }

    [Fact]
    public void NormalizeLayout_NullCollections_Recovered()
    {
        var layout = new TrayMenuLayout { Root = null! };
        var child = new TrayMenuNode { Kind = "block", BlockKind = TrayMenuBlockKind.Services, Children = null! };

        var result = TrayMenuResolver.NormalizeLayout(layout);
        layout.Root.Add(child);
        var result2 = TrayMenuResolver.NormalizeLayout(layout);

        Assert.NotNull(result.Root);
        Assert.NotNull(result2.Root[0].Children);
        Assert.Equal(TrayMenuBlockStyle.Submenu, result2.Root[0].Style);
    }

    [Fact]
    public void BuildEffective_HidesInvisibleBlocks_KeepsVisible()
    {
        var layout = new TrayMenuLayout
        {
            Root =
            {
                TrayMenuLayouts.Block(TrayMenuBlockKind.Favorites),
                new TrayMenuNode { Kind = "block", BlockKind = TrayMenuBlockKind.Recent, Visible = false },
                TrayMenuLayouts.Block(TrayMenuBlockKind.Services),
            },
        };

        var result = TrayMenuResolver.BuildEffective(layout);

        Assert.Equal(2, result.Root.Count);
        Assert.Equal(TrayMenuBlockKind.Favorites, result.Root[0].BlockKind);
        Assert.Equal(TrayMenuBlockKind.Services, result.Root[1].BlockKind);
    }

    [Fact]
    public void BuildEffective_NullLayout_ReturnsDefault()
    {
        var result = TrayMenuResolver.BuildEffective(null!);

        Assert.Equal(3, result.Root.Count);
    }

    /// <summary>Временный каталог, удаляемый при освобождении.</summary>
    private static TempDirectory TempDir() => new();

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cm_tray_menu_tests_" + Guid.NewGuid().ToString("N"));
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
