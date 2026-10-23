namespace Workspace1C.Models;

/// <summary>
/// Компоновка контекстного меню базы (issue #56): пользовательская структура —
/// порядок, видимость и группировка пунктов. Хранится в отдельном читаемом
/// JSON-файле <c>context_menu_layout.json</c> рядом с настройками (см.
/// <see cref="Workspace1C.Services.ContextMenuLayoutStore"/>).
/// </summary>
public sealed class ContextMenuLayout
{
    /// <summary>Версия схемы файла компоновки (для будущих миграций).</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Корневые узлы меню в порядке отображения.</summary>
    public List<ContextMenuNode> Root { get; set; } = new();
}

/// <summary>
/// Узел дерева компоновки контекстного меню: либо ссылка на пункт каталога
/// (<c>Kind == "item"</c>), либо группа-подменю (<c>Kind == "group"</c>), либо
/// разделитель (<c>Kind == "separator"</c>, issue #374). Единый класс вместо
/// иерархии — так JSON-файл проще править вручную и не нужен полиморфный разбор.
/// </summary>
public sealed class ContextMenuNode
{
    /// <summary>Тип узла: "item" — пункт каталога, "group" — подменю, "separator" — разделитель.</summary>
    public string Kind { get; set; } = "item";

    /// <summary>Идентификатор пункта каталога (для <c>Kind == "item"</c>).</summary>
    public string? ItemId { get; set; }

    /// <summary>Идентификатор стандартной группы (для <c>Kind == "group"</c>; null — пользовательская).</summary>
    public string? GroupId { get; set; }

    /// <summary>Пользовательский заголовок (переопределяет локализованный).</summary>
    public string? CustomTitle { get; set; }

    /// <summary>Ключ иконки из программы (PackIcon Kind для WPF / Icon* для Avalonia).</summary>
    public string? IconKey { get; set; }

    /// <summary>Цвет иконки (#RRGGBB).</summary>
    public string? IconColor { get; set; }

    /// <summary>Путь к внешнему файлу иконки (назначается «извне»).</summary>
    public string? ExternalIconPath { get; set; }

    /// <summary>Видимость узла (скрытые пункты не показываются в меню).</summary>
    public bool Visible { get; set; } = true;

    /// <summary>Рисовать разделитель перед узлом.</summary>
    public bool SeparatorBefore { get; set; }

    /// <summary>Дочерние узлы (для <c>Kind == "group"</c>).</summary>
    public List<ContextMenuNode> Children { get; set; } = new();
}