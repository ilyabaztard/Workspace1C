using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Workspace1C.Models;

/// <summary>
/// Компоновка меню системного трея (issue #62): пользовательская структура верхнего
/// уровня — порядок, видимость и стиль блоков («Избранные», «Недавние», «Службы»,
/// пользовательские группы, разделители). Хранится в читаемом JSON-файле
/// <c>tray_menu_layout.json</c> рядом с настройками (см.
/// <see cref="Workspace1C.Services.TrayMenuLayoutStore"/>).
/// <para>
/// Фиксированные пункты «Открыть программу», «Настройки», разделитель и «Выход»
/// в компоновке отсутствуют — их всегда добавляют адаптеры трея (WPF/Avalonia).
/// </para>
/// </summary>
public sealed class TrayMenuLayout
{
    /// <summary>Версия схемы файла компоновки (для будущих миграций).</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Корневые узлы меню в порядке отображения.</summary>
    public List<TrayMenuNode> Root { get; set; } = new();
}

/// <summary>
/// Вид блока меню трея: стандартные блоки, содержимое которых формируют адаптеры
/// (избранные, недавние, службы), и пользовательские группы.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TrayMenuBlockKind
{
    /// <summary>Избранные базы (закладки Alt+1…9) — наполняются динамически.</summary>
    Favorites,

    /// <summary>Недавние базы (или выбранная база) — наполняются динамически.</summary>
    Recent,

    /// <summary>Подменю «Службы» — наполняется динамически из списка служб.</summary>
    Services,

    /// <summary>Пользовательская группа с собственным заголовком и иконкой.</summary>
    UserGroup
}

/// <summary>
/// Стиль отображения блока: «Блок меню» (заголовок, под ним пункты — как шапка
/// избранных/недавних сейчас) или «Подменю» (пункт, раскрывающий вложенное меню).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TrayMenuBlockStyle
{
    /// <summary>Блок меню: текст-заголовок, под ним пункты.</summary>
    Block,

    /// <summary>Подменю: пункт, раскрывающий вложенное меню.</summary>
    Submenu
}

/// <summary>
/// Узел дерева компоновки меню трея: либо блок (<c>Kind == "block"</c>), либо
/// разделитель (<c>Kind == "separator"</c>). Единый класс вместо иерархии — так
/// JSON-файл проще править вручную и не нужен полиморфный разбор с дискриминатором.
/// </summary>
public sealed class TrayMenuNode
{
    /// <summary>Тип узла: "block" — блок, "separator" — разделитель.</summary>
    public string Kind { get; set; } = "block";

    /// <summary>Вид блока (только для <c>Kind == "block"</c>).</summary>
    public TrayMenuBlockKind? BlockKind { get; set; }

    /// <summary>Идентификатор пользовательской группы (для <see cref="TrayMenuBlockKind.UserGroup"/>).</summary>
    public string? GroupId { get; set; }

    /// <summary>Пользовательский заголовок (переопределяет локализованный).</summary>
    public string? CustomTitle { get; set; }

    /// <summary>Ключ иконки из программы (PackIcon Kind для WPF / Icon* для Avalonia).</summary>
    public string? IconKey { get; set; }

    /// <summary>Цвет иконки (#RRGGBB).</summary>
    public string? IconColor { get; set; }

    /// <summary>Путь к внешнему файлу иконки (назначается «извне»).</summary>
    public string? ExternalIconPath { get; set; }

    /// <summary>Видимость блока (скрытые блоки не показываются в меню).</summary>
    public bool Visible { get; set; } = true;

    /// <summary>
    /// Стиль блока (см. <see cref="TrayMenuBlockStyle"/>). Пустое значение в файле
    /// означает «по умолчанию для вида блока» (Favorites/Recent — Block, Services —
    /// Submenu); результат <see cref="Workspace1C.Services.TrayMenuResolver.NormalizeLayout"/>
    /// всегда заполнен.
    /// </summary>
    public TrayMenuBlockStyle? Style { get; set; }

    /// <summary>Рисовать разделитель перед узлом (артефакт совместимости; в текущей
    /// схеме разделители — самостоятельные узлы <c>Kind == "separator"</c>).</summary>
    public bool SeparatorBefore { get; set; }

    /// <summary>Дочерние узлы (зарезервировано; для блоков трея не используются).</summary>
    public List<TrayMenuNode> Children { get; set; } = new();
}
