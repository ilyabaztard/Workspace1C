using System;
using System.Collections.Generic;
using System.Text;
using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Вид пункта меню трея (issue #62), собранного из компоновки
/// <see cref="TrayMenuLayout"/>: фиксированные пункты «Открыть программу»,
/// «Настройки», разделитель и «Выход» добавляются построителем первым и
/// последними, а блоки компоновки описываются как
/// <see cref="TrayItemKind.Header"/> (стиль «Блок меню» — заголовок, под ним
/// содержимое) или <see cref="TrayItemKind.Submenu"/> (стиль «Подменю»).
/// Содержимое динамических блоков (избранные, недавние, службы) в чистую
/// модель не попадает — его добавляют платформенные адаптеры
/// (<c>MainWindow.Tray.cs</c>/<c>MainWindow.Avalonia.Tray.cs</c>), читая
/// <see cref="Block"/>. Модель без платформенных типов — пригодна для
/// юнит-тестов построения.
/// </summary>
public enum TrayItemKind
{
    /// <summary>Фиксированный пункт «Открыть программу» (всегда первый).</summary>
    Open,

    /// <summary>Заголовок блока в стиле «Блок меню» (под ним адаптер рисует содержимое).</summary>
    Header,

    /// <summary>Простой пункт содержимого (дети пользовательских групп).</summary>
    Item,

    /// <summary>Подменю: пункт, раскрывающий вложенное меню.</summary>
    Submenu,

    /// <summary>Разделитель.</summary>
    Separator,

    /// <summary>Фиксированный пункт «Настройки» (всегда перед последним разделителем).</summary>
    Settings,

    /// <summary>Фиксированный пункт «Выход» (всегда последний).</summary>
    Exit
}

/// <summary>
/// Пункт меню трея (issue #62): плоское описание структуры верхнего уровня,
/// пригодное для построения системного меню на обеих платформах (WinForms и
/// Avalonia). Никаких платформенных типов — только enum, строки и JSON-ключи
/// блоков (<see cref="Block"/>), по которым адаптеры добавляют динамическое
/// содержимое (избранные/недавние базы, службы).
/// </summary>
public sealed class TrayMenuItem
{
    /// <summary>Вид пункта (см. <see cref="TrayItemKind"/>).</summary>
    public TrayItemKind Kind { get; init; }

    /// <summary>Готовый отображаемый текст (локализован построителем).</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Вид блока компоновки (для <see cref="TrayItemKind.Header"/> и
    /// <see cref="TrayItemKind.Submenu"/>); для остальных — null.</summary>
    public TrayMenuBlockKind? Block { get; init; }

    /// <summary>Ключ иконки блока (пользовательские группы); пусто — без иконки.</summary>
    public string IconKey { get; init; } = string.Empty;

    /// <summary>Цвет иконки #RRGGBB (пользовательские группы); пусто — цвет по умолчанию.</summary>
    public string IconColor { get; init; } = string.Empty;

    /// <summary>Дочерние пункты подменю (для <see cref="TrayItemKind.Submenu"/>).</summary>
    public IReadOnlyList<TrayMenuItem> Children { get; init; } = Array.Empty<TrayMenuItem>();

    /// <summary>True — пункт является разделителем.</summary>
    public bool IsSeparator => Kind == TrayItemKind.Separator;
}

/// <summary>
/// Чистый построитель структуры меню трея (issue #62): из эффективной компоновки
/// <see cref="TrayMenuLayout"/> (скрытые узлы отброшены —
/// <see cref="TrayMenuResolver.BuildEffective"/>) возвращает плоский список
/// пунктов верхнего уровня с фиксированными пунктами «Открыть программу» /
/// «Настройки» / «Выход» всегда на местах. Класс не имеет платформенных
/// зависимостей: меню строят адаптеры в <c>MainWindow.Tray.cs</c> (WinForms) и
/// <c>MainWindow.Avalonia.Tray.cs</c> (NativeMenu) по этому списку, подставляя
/// динамическое содержимое блоков.
/// </summary>
public sealed class TrayMenuBuilder
{
    private readonly Func<string, string> _t;

    /// <param name="t">Резолвер локализованного текста по ключу; по умолчанию — <see cref="LocalizationManager.T"/>.</param>
    public TrayMenuBuilder(Func<string, string>? t = null)
        => _t = t ?? (key => LocalizationManager.T(key));

    /// <summary>
    /// Строит структуру меню трея верхнего уровня:
    /// <list type="number">
    /// <item>«Открыть программу» (<see cref="TrayItemKind.Open"/>) — фиксированный первый;</item>
    /// <item>узлы эффективной компоновки в порядке следования: разделитель —
    /// <see cref="TrayItemKind.Separator"/>; блок «Блок меню» — <see cref="TrayItemKind.Header"/>
    /// (содержимое подставляет адаптер по <see cref="TrayMenuItem.Block"/>); блок «Подменю» —
    /// <see cref="TrayItemKind.Submenu"/> с дочерними узлами;</item>
    /// <item>фиксированный хвост: разделитель, «Настройки» (<see cref="TrayItemKind.Settings"/>),
    /// разделитель, «Выход» (<see cref="TrayItemKind.Exit"/>).</item>
    /// </list>
    /// </summary>
    /// <param name="layout">Компоновка (или null — стандартная).</param>
    public List<TrayMenuItem> Build(TrayMenuLayout? layout)
    {
        var list = new List<TrayMenuItem>
        {
            // Фиксированный первый пункт — «Открыть программу» (issue #62).
            new() { Kind = TrayItemKind.Open, Text = _t("Main.TrayOpen") }
        };

        var effective = TrayMenuResolver.BuildEffective(layout ?? TrayMenuLayouts.Default());
        foreach (var node in effective.Root)
            AppendNode(list, node);

        // Фиксированный хвост: разделитель, «Настройки», разделитель, «Выход»
        // (как в старом меню трея; пункты не редактируются — issue #62).
        list.Add(Separator());
        list.Add(new TrayMenuItem { Kind = TrayItemKind.Settings, Text = _t("Main.Settings") });
        list.Add(Separator());
        list.Add(new TrayMenuItem { Kind = TrayItemKind.Exit, Text = _t("Main.Exit") });
        return list;
    }

    /// <summary>
    /// Отпечаток компоновки для «ленивой» пересборки меню (Avalonia
    /// <c>RefreshTrayMenu</c>): строка, однозначно отражающая структуру
    /// верхнего уровня (виды пунктов и блоков), а также ключ/цвет иконки
    /// пользовательских групп (issue #62). Если пользователь изменил
    /// компоновку в редакторе — в том числе только иконку группы, — или
    /// настройки («Правка структуры») сменились, отпечаток меняется,
    /// и меню пересобирается.
    /// </summary>
    public static string LayoutSignature(TrayMenuLayout? layout)
    {
        var items = new TrayMenuBuilder(_ => string.Empty).Build(layout);
        var sb = new StringBuilder();
        foreach (var item in items)
        {
            sb.Append((int)item.Kind).Append(':');
            if (item.Block is { } block)
                sb.Append((int)block);
            sb.Append('~').Append(item.IconKey).Append('~').Append(item.IconColor);
            sb.Append(';');
        }
        return sb.ToString();
    }

    private void AppendNode(List<TrayMenuItem> list, TrayMenuNode? node)
    {
        if (node is null)
            return;

        // Узел-разделитель (в т.ч. артефакт SeparatorBefore из старой схемы).
        if (node.SeparatorBefore)
            list.Add(Separator());
        if (string.Equals(node.Kind, "separator", StringComparison.Ordinal))
        {
            list.Add(Separator());
            return;
        }
        if (!string.Equals(node.Kind, "block", StringComparison.Ordinal))
            return;

        var style = node.Style ?? TrayMenuLayouts.DefaultStyle(node.BlockKind);
        var text = BlockText(node);
        if (style == TrayMenuBlockStyle.Block)
        {
            // «Блок меню»: текст-заголовок, под ним содержимое. Содержимое
            // динамических блоков поставляет адаптер (избранные/недавние/
            // службы); для пользовательской группы — дети компоновки.
            list.Add(new TrayMenuItem
            {
                Kind = TrayItemKind.Header,
                Text = text,
                Block = node.BlockKind,
                IconKey = node.IconKey ?? string.Empty,
                IconColor = node.IconColor ?? string.Empty,
            });
            foreach (var child in node.Children)
                AppendNode(list, child);
            return;
        }

        // «Подменю»: пункт, раскрывающий вложенное меню из детей компоновки.
        var children = new List<TrayMenuItem>();
        foreach (var child in node.Children)
            AppendNode(children, child);
        list.Add(new TrayMenuItem
        {
            Kind = TrayItemKind.Submenu,
            Text = text,
            Block = node.BlockKind,
            IconKey = node.IconKey ?? string.Empty,
            IconColor = node.IconColor ?? string.Empty,
            Children = children,
        });
    }

    /// <summary>Заголовок блока: пользовательский (группа) или локализованное имя блока.</summary>
    private string BlockText(TrayMenuNode node)
    {
        if (!string.IsNullOrWhiteSpace(node.CustomTitle))
            return node.CustomTitle;
        return node.BlockKind switch
        {
            TrayMenuBlockKind.Favorites => _t("TrayMenu.BlockFavorites"),
            TrayMenuBlockKind.Recent => _t("TrayMenu.BlockRecent"),
            TrayMenuBlockKind.Services => _t("TrayMenu.BlockServices"),
            _ => _t("TrayMenu.NewGroup"),
        };
    }

    private static TrayMenuItem Separator() => new() { Kind = TrayItemKind.Separator };
}