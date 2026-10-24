using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты чистого построителя структуры меню трея (issue #62): из компоновки
/// <see cref="TrayMenuLayout"/> строятся пункты верхнего уровня с фиксированными
/// «Открыть программу»/«Настройки»/«Выход» всегда на местах, скрытые узлы
/// пропадают, стиль блока («Блок меню»/«Подменю») превращается в
/// <see cref="TrayItemKind.Header"/> или <see cref="TrayItemKind.Submenu"/>.
/// Тексты пунктов локализованы через подставленный резолвер ключей (в тесте —
/// сам ключ), платформенные типы не участвуют.
/// </summary>
public sealed class TrayMenuBuilderTests
{
    /// <summary>Построитель с резолвером «ключ → ключ» (тексты предсказуемы).</summary>
    private static TrayMenuBuilder Builder() => new(k => k);

    [Fact]
    public void TrayMenuBuilder_DefaultLayout_PinnedItemsFirstAndLast_BlocksInOrder()
    {
        var items = Builder().Build(layout: null);

        var kinds = items.Select(i => i.Kind).ToList();
        // Открыть → блоки (Избранные, Недавние, Службы) → разделитель → Настройки →
        // разделитель → Выход: фиксированные пункты первым и последними (issue #62).
        Assert.Equal(
        [
            TrayItemKind.Open,
            TrayItemKind.Header,
            TrayItemKind.Header,
            TrayItemKind.Submenu,
            TrayItemKind.Separator,
            TrayItemKind.Settings,
            TrayItemKind.Separator,
            TrayItemKind.Exit,
        ], kinds);

        Assert.Equal("Main.TrayOpen", items[0].Text);
        Assert.Equal(TrayMenuBlockKind.Favorites, items[1].Block);
        Assert.Equal(TrayMenuBlockKind.Recent, items[2].Block);
        Assert.Equal(TrayMenuBlockKind.Services, items[3].Block);
        Assert.Equal("Main.Settings", items[5].Text);
        Assert.Equal("Main.Exit", items[7].Text);
    }

    [Fact]
    public void TrayMenuBuilder_StyleBlock_ProducesHeaderAndItems_StyleSubmenu_ProducesSubmenu()
    {
        var layout = new TrayMenuLayout
        {
            Root =
            {
                // Службы в стиле «Блок меню»: заголовок, под ним содержимое.
                new TrayMenuNode
                {
                    Kind = "block",
                    BlockKind = TrayMenuBlockKind.Services,
                    Style = TrayMenuBlockStyle.Block,
                },
                // Пользовательская группа в стиле «Блок меню» — заголовок.
                new TrayMenuNode
                {
                    Kind = "block",
                    BlockKind = TrayMenuBlockKind.UserGroup,
                    CustomTitle = "Мои пункты",
                    Style = TrayMenuBlockStyle.Block,
                },
                // Пользовательская группа в стиле «Подменю» — пункт с детьми.
                new TrayMenuNode
                {
                    Kind = "block",
                    BlockKind = TrayMenuBlockKind.UserGroup,
                    CustomTitle = "Группа-меню",
                    Style = TrayMenuBlockStyle.Submenu,
                    Children = { TrayMenuLayouts.Separator() },
                },
            },
        };

        var items = Builder().Build(layout);

        // Службы (Block) → Header с видом блока Services.
        var services = items.First(i => i.Block == TrayMenuBlockKind.Services);
        Assert.Equal(TrayItemKind.Header, services.Kind);
        Assert.Equal("TrayMenu.BlockServices", services.Text);

        // Пользовательская группа (Block) → Header с пользовательским заголовком.
        var groupHeader = items.First(i => i.Block == TrayMenuBlockKind.UserGroup && i.Kind == TrayItemKind.Header);
        Assert.Equal("Мои пункты", groupHeader.Text);

        // Пользовательская группа (Submenu) → Submenu с детьми (разделитель внутри).
        var groupSubmenu = items.First(i => i.Block == TrayMenuBlockKind.UserGroup && i.Kind == TrayItemKind.Submenu);
        Assert.Equal("Группа-меню", groupSubmenu.Text);
        Assert.Single(groupSubmenu.Children);
        Assert.True(groupSubmenu.Children[0].IsSeparator);
    }

    [Fact]
    public void TrayMenuBuilder_HiddenNode_Skipped_SeparatorBefore_Respected()
    {
        var layout = new TrayMenuLayout
        {
            Root =
            {
                // Скрытый блок не попадает в меню вовсе.
                new TrayMenuNode
                {
                    Kind = "block",
                    BlockKind = TrayMenuBlockKind.Favorites,
                    Visible = false,
                },
                // Разделитель перед блоком (артефакт совместимости) уважается.
                new TrayMenuNode
                {
                    Kind = "block",
                    BlockKind = TrayMenuBlockKind.Recent,
                    SeparatorBefore = true,
                },
                // Узел-разделитель даёт разделитель.
                TrayMenuLayouts.Separator(),
                new TrayMenuNode
                {
                    Kind = "block",
                    BlockKind = TrayMenuBlockKind.Services,
                },
            },
        };

        var items = Builder().Build(layout);

        Assert.DoesNotContain(items, i => i.Block == TrayMenuBlockKind.Favorites);
        var hasSeparators = items.Count(i => i.IsSeparator);
        // 2 разделителя из компоновки (SeparatorBefore + узел) + 2 фиксированных хвоста.
        Assert.Equal(4, hasSeparators);

        // Порядок: Open → Sep(Recent.SeparatorBefore) → Header(Recent) → Sep(узел) → Submenu(Services).
        var firstSeparator = items[1];
        Assert.True(firstSeparator.IsSeparator);
        Assert.Equal(TrayMenuBlockKind.Recent, items[2].Block);
        Assert.True(items[3].IsSeparator);
        Assert.Equal(TrayMenuBlockKind.Services, items[4].Block);
    }

    [Fact]
    public void TrayMenuBuilder_LayoutSignature_ReflectsLayoutChanges()
    {
        var layout = new TrayMenuLayout
        {
            Root =
            {
                TrayMenuLayouts.Block(TrayMenuBlockKind.Favorites),
                TrayMenuLayouts.Block(TrayMenuBlockKind.Services),
            },
        };
        var baseSignature = TrayMenuBuilder.LayoutSignature(layout);

        // Смена стиля блока меняет отпечаток (RefreshTrayMenu пересоберёт меню).
        layout.Root[1].Style = TrayMenuBlockStyle.Block;
        Assert.NotEqual(baseSignature, TrayMenuBuilder.LayoutSignature(layout));

        // Скрытие блока тоже меняет отпечаток.
        layout.Root[0].Visible = false;
        Assert.NotEqual(baseSignature, TrayMenuBuilder.LayoutSignature(layout));

        // Тот же состав даёт стабильный отпечаток.
        Assert.Equal(baseSignature, TrayMenuBuilder.LayoutSignature(new TrayMenuLayout
        {
            Root =
            {
                TrayMenuLayouts.Block(TrayMenuBlockKind.Favorites),
                new TrayMenuNode { Kind = "block", BlockKind = TrayMenuBlockKind.Services, Style = TrayMenuBlockStyle.Submenu },
            },
        }));
    }
}