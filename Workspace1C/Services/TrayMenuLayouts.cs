using System.Collections.Generic;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Построитель стандартной компоновки меню трея (issue #62): воспроизводит текущую
/// структуру трея без пункта «Синхронизировать базы», чтобы при выключенной правке
/// меню выглядело ровно как сейчас (обратная совместимость).
/// <para>
/// Фиксированные пункты «Открыть программу», «Настройки», разделитель и «Выход»
/// в компоновке отсутствуют — их всегда добавляют адаптеры трея (WPF/Avalonia).
/// </para>
/// </summary>
public static class TrayMenuLayouts
{
    /// <summary>
    /// Стандартная компоновка меню трея: блок «Избранные базы», блок «Недавние базы»,
    /// блок «Службы» (подменю). Пункт синхронизации с ibases в трее убран (issue #62).
    /// </summary>
    public static TrayMenuLayout Default()
    {
        var layout = new TrayMenuLayout
        {
            SchemaVersion = 1,
            Root =
            [
                Block(TrayMenuBlockKind.Favorites),
                Block(TrayMenuBlockKind.Recent),
                Block(TrayMenuBlockKind.Services),
            ],
        };
        return layout;
    }

    /// <summary>Стиль блока по умолчанию: Favorites/Recent — «Блок меню»,
    /// Services и пользовательские группы — «Подменю».</summary>
    public static TrayMenuBlockStyle DefaultStyle(TrayMenuBlockKind? kind) => kind switch
    {
        TrayMenuBlockKind.Favorites => TrayMenuBlockStyle.Block,
        TrayMenuBlockKind.Recent => TrayMenuBlockStyle.Block,
        // Службы (текущий вид трея) и пользовательские группы — подменю.
        _ => TrayMenuBlockStyle.Submenu,
    };

    /// <summary>Узел-блок стандартной компоновки (видим, стиль по умолчанию).</summary>
    public static TrayMenuNode Block(TrayMenuBlockKind kind) => new()
    {
        Kind = "block",
        BlockKind = kind,
        Visible = true,
        Style = DefaultStyle(kind),
    };

    /// <summary>Узел-разделитель.</summary>
    public static TrayMenuNode Separator() => new()
    {
        Kind = "separator",
    };
}
