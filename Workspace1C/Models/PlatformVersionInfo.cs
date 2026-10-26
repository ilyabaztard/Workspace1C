namespace Workspace1C.Models;

/// <summary>
/// Установленная версия платформы 1С с путём к каталогу версии.
/// </summary>
public class PlatformVersionInfo
{
    /// <summary>Отображаемая строка, например «8.3.27.2214 (64)».</summary>
    public string Display { get; set; } = string.Empty;

    /// <summary>Полный путь к папке версии (каталог с bin/).</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// True — установлен только тонкий клиент (в bin есть 1cv8c, но нет полного 1cv8):
    /// конфигуратор, сервер и часть возможностей недоступны (issue #33).
    /// </summary>
    public bool IsThinClient { get; set; }

    /// <summary>
    /// Полное отображаемое имя для списков: «8.3.27.2214 (x64)» или с локализованным
    /// суффиксом «(тонкий клиент)» для тонких клиентов (issue #33). Используется
    /// в списках платформ (диалог выбора версии, настройки, «Сменить платформу»,
    /// WPF и Avalonia) через <see cref="Services.PlatformDisplayFormatter"/>, чтобы
    /// суффикс и оранжевая подсветка гарантированно попадали в UI.
    /// </summary>
    public string DisplayName => Services.PlatformDisplayFormatter.Format(this);

    public override string ToString() => Display;
}
