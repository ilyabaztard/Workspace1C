using System;
using System.Collections.Generic;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Резолвер компоновки меню трея (issue #62): выбирает эффективную компоновку по
/// настройкам (правка включена/выключена), нормализует файл компоновки и готовит
/// её к построению (фильтр видимости). Платформенно-нейтральная логика, общая для
/// построителей WPF и Avalonia.
/// </summary>
public static class TrayMenuResolver
{
    /// <summary>
    /// Возвращает компоновку для построения меню трея:
    /// <list type="bullet">
    /// <item>правка выключена (по умолчанию) — стандартная компоновка (обратная совместимость);</item>
    /// <item>правка включена и файл компоновки есть — пользовательская компоновка;</item>
    /// <item>правка включена, файла нет — стандартная компоновка (файл создаст редактор).</item>
    /// </list>
    /// </summary>
    public static TrayMenuLayout Resolve(AppSettings? settings, ITrayMenuLayoutStore? store)
    {
        if (settings is null || !settings.TrayMenuEditingEnabled)
            return TrayMenuLayouts.Default();
        var layout = store?.Load();
        return layout ?? TrayMenuLayouts.Default();
    }

    /// <summary>
    /// Приводит компоновку к безопасному виду: восстанавливает null-коллекции и
    /// null-строки, отбрасывает узлы с неизвестным типом (<c>Kind</c>) и неизвестным
    /// видом блока (<see cref="TrayMenuBlockKind"/>), заполняет стиль блока по умолчанию
    /// (<see cref="TrayMenuLayouts.DefaultStyle"/>; если стиль не задан вовсе —
    /// для Favorites/Recent «Блок меню», для Services — «Подменю») и признак видимости
    /// (<c>Visible = true</c>). При null-компоновке возвращает стандартную.
    /// </summary>
    public static TrayMenuLayout NormalizeLayout(TrayMenuLayout? layout)
    {
        if (layout is null)
            return TrayMenuLayouts.Default();
        layout.SchemaVersion = 1;
        layout.Root ??= new List<TrayMenuNode>();
        layout.Root = NormalizeList(layout.Root);
        return layout;
    }

    /// <summary>
    /// Готовит компоновку к построению меню: возвращает копию дерева, из которой
    /// исключены скрытые узлы (<c>Visible == false</c>) верхнего уровня. Скрытый
    /// блок не просто не рисуется — для построителя его как бы нет (главный кейс —
    /// блок «Недавние», когда недавних нет вовсе, и пользователь скрыл блок «Избранные»).
    /// Стандартные динамические содержимые блоков адаптеры добавляют сами.
    /// </summary>
    public static TrayMenuLayout BuildEffective(TrayMenuLayout layout)
    {
        var source = layout ?? TrayMenuLayouts.Default();
        var result = new TrayMenuLayout { SchemaVersion = source.SchemaVersion };
        foreach (var node in source.Root)
        {
            if (node is null || !node.Visible)
                continue;
            result.Root.Add(CloneNode(node));
        }
        return result;
    }

    /// <summary>Нормализует список узлов: прогоняет каждый через <see cref="NormalizeNode"/>.</summary>
    private static List<TrayMenuNode> NormalizeList(List<TrayMenuNode>? nodes)
    {
        var result = new List<TrayMenuNode>();
        if (nodes is null)
            return result;
        foreach (var node in nodes)
        {
            var normalized = NormalizeNode(node);
            if (normalized is not null)
                result.Add(normalized);
        }
        return result;
    }

    private static TrayMenuNode? NormalizeNode(TrayMenuNode? node)
    {
        if (node is null)
            return null;
        node.Kind ??= "block";
        node.Children ??= new List<TrayMenuNode>();
        node.CustomTitle ??= string.Empty;
        node.IconKey ??= string.Empty;
        node.IconColor ??= string.Empty;
        node.ExternalIconPath ??= string.Empty;

        // Разделитель — самостоятельный узел: имя/группа не нужны.
        if (string.Equals(node.Kind, "separator", StringComparison.Ordinal))
            return node;

        // Допустим только узел-блок; всё остальное (в т.ч. неизвестные Kind) отбрасываем.
        if (!string.Equals(node.Kind, "block", StringComparison.Ordinal))
            return null;

        // Блок обязан иметь известный вид.
        if (node.BlockKind is null || !Enum.IsDefined(node.BlockKind.Value))
            return null;

        // Видимость по умолчанию — показан.
        // (Visible уже инициализирован true в модели; для файлов без поля — тоже true.)

        // Стиль по умолчанию: не задан — по виду блока (Favorites/Recent — Block,
        // Services/UserGroup — Submenu).
        if (node.Style is null)
            node.Style = TrayMenuLayouts.DefaultStyle(node.BlockKind);

        // Вложенные узлы (зарезервировано — сейчас блоки трея двухуровневые).
        node.Children = NormalizeList(node.Children);
        return node;
    }

    /// <summary>Глубокая копия узла компоновки (построитель не мутирует исходный layout).</summary>
    private static TrayMenuNode CloneNode(TrayMenuNode node)
    {
        var copy = new TrayMenuNode
        {
            Kind = node.Kind,
            BlockKind = node.BlockKind,
            GroupId = node.GroupId,
            CustomTitle = node.CustomTitle,
            IconKey = node.IconKey,
            IconColor = node.IconColor,
            ExternalIconPath = node.ExternalIconPath,
            Visible = node.Visible,
            Style = node.Style,
            SeparatorBefore = node.SeparatorBefore,
        };
        foreach (var child in node.Children ?? new List<TrayMenuNode>())
        {
            // Скрытые узлы (в т.ч. дочерние блоки внутри групп) в меню не попадают.
            if (child is null || !child.Visible)
                continue;
            copy.Children.Add(CloneNode(child));
        }
        return copy;
    }
}
