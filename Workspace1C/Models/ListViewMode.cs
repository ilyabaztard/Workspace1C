namespace Workspace1C.Models;

/// <summary>Режим списка на главном окне: все базы / избранное / недавние / запущенные.</summary>
public enum ListViewMode
{
    All,
    Favorites,
    Recent,
    /// <summary>Показывать только запущенные базы (issue #339).</summary>
    Running
}

/// <summary>
/// Канонические строковые значения режима списка и вспомогательные методы разбора.
/// Строки стабильны и не зависят от локали — они хранятся в <c>settings.json</c>
/// (<see cref="AppSettings.ListMode"/>, issue #52), поэтому смена языка интерфейса
/// не ломает сохранённый режим.
/// </summary>
public static class ListViewModes
{
    /// <summary>Каноническое значение режима «Все базы».</summary>
    public const string All = "All";

    /// <summary>Каноническое значение режима «Избранное».</summary>
    public const string Favorites = "Favorites";

    /// <summary>Каноническое значение режима «Недавние».</summary>
    public const string Recent = "Recent";

    /// <summary>Каноническое значение режима «Запущенные» (issue #339).</summary>
    public const string Running = "Running";

    /// <summary>Режим по умолчанию — «Все базы».</summary>
    public const string Default = All;

    /// <summary>
    /// Приводит произвольное значение к каноническому режиму списка.
    /// Пустые/неизвестные значения возвращают <see cref="Default"/> («Все базы»).
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Default;

        var v = value.Trim();
        if (string.Equals(v, Favorites, StringComparison.OrdinalIgnoreCase))
            return Favorites;
        if (string.Equals(v, Recent, StringComparison.OrdinalIgnoreCase))
            return Recent;
        if (string.Equals(v, Running, StringComparison.OrdinalIgnoreCase))
            return Running;
        return All;
    }

    /// <summary>Разбирает строковое значение в <see cref="ListViewMode"/>; неизвестное — <see cref="ListViewMode.All"/>.</summary>
    public static ListViewMode Parse(string? value)
        => Normalize(value) switch
        {
            Favorites => ListViewMode.Favorites,
            Recent => ListViewMode.Recent,
            Running => ListViewMode.Running,
            _ => ListViewMode.All
        };

    /// <summary>Строковое представление режима для сохранения в настройках.</summary>
    public static string ToSettingString(ListViewMode mode)
        => mode switch
        {
            ListViewMode.Favorites => Favorites,
            ListViewMode.Recent => Recent,
            ListViewMode.Running => Running,
            _ => All
        };
}
