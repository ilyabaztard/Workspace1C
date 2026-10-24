using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты стандартной компоновки меню трея (issue #62): блоки «Избранные», «Недавние»,
/// «Службы» без пункта синхронизации.
/// </summary>
public sealed class TrayMenuLayoutTests
{
    [Fact]
    public void Default_HasNoSyncItem_HasFavoritesRecentServicesBlocks()
    {
        var layout = TrayMenuLayouts.Default();

        // Ровно три блока верхнего уровня (разделители и пользовательские группы
        // в стандартной компоновке отсутствуют).
        Assert.Equal(3, layout.Root.Count);
        Assert.All(layout.Root, n => Assert.Equal("block", n.Kind));
        Assert.All(layout.Root, n => Assert.True(n.Visible));

        var kinds = layout.Root.Select(n => n.BlockKind!.Value).ToList();
        Assert.Contains(TrayMenuBlockKind.Favorites, kinds);
        Assert.Contains(TrayMenuBlockKind.Recent, kinds);
        Assert.Contains(TrayMenuBlockKind.Services, kinds);

        // Нет ничего, что можно было бы интерпретировать как «Синхронизировать базы»
        // (issue #62): пункт синхронизации убран из трея на обеих платформах.
        Assert.DoesNotContain(TrayMenuBlockKind.UserGroup, kinds);
        Assert.DoesNotContain(layout.Root, n => string.Equals(n.CustomTitle, "SyncWithIbases", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Default_Styles_FavoritesAndRecentBlock_ServicesSubmenu()
    {
        var layout = TrayMenuLayouts.Default();

        var fav = layout.Root.First(n => n.BlockKind == TrayMenuBlockKind.Favorites);
        var recent = layout.Root.First(n => n.BlockKind == TrayMenuBlockKind.Recent);
        var services = layout.Root.First(n => n.BlockKind == TrayMenuBlockKind.Services);

        Assert.Equal(TrayMenuBlockStyle.Block, fav.Style);
        Assert.Equal(TrayMenuBlockStyle.Block, recent.Style);
        Assert.Equal(TrayMenuBlockStyle.Submenu, services.Style);
    }
}
