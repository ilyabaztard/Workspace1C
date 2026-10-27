using System;
using System.Collections.Generic;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Хранилище и расчёт ширины колонок вкладок окна «Серверы 1С» (issue #75).
/// Чистая логика без платформенных зависимостей — используется обеими платформами
/// (WPF/Windows и Avalonia/Linux) и покрыта юнит-тестами.
/// <para>
/// Задачи, решаемые классом:
/// <list type="bullet">
/// <item>минимальная ширина колонки по содержимому ЗАГОЛОВКА: берётся худший случай
/// русской/английской локали (длина строки × коэффициент на символ) плюс запас
/// на стрелку сортировки ▲/▼ и горизонтальные отступы заголовка — чтобы заголовок
/// не обрезался по умолчанию;</item>
/// <item>восстановление сохранённых ширин по ключу вкладки
/// (<see cref="SessionsKey"/>, <see cref="ConnectionsKey"/>, <see cref="LocksKey"/>,
/// <see cref="ProcessesKey"/>) с изоляцией вкладок друг от друга;</item>
/// <item>нормализация при применении: сохранённое значение, меньшее минимального
/// или некорректное (NaN/бесконечность/≤ 0), отбрасывается в пользу дефолта.</item>
/// </list>
/// </para>
/// </summary>
public static class ServerMonitorColumnWidthsStore
{
    /// <summary>Ключ вкладки «Сеансы».</summary>
    public const string SessionsKey = "Sessions";

    /// <summary>Ключ вкладки «Соединения».</summary>
    public const string ConnectionsKey = "Connections";

    /// <summary>Ключ вкладки «Блокировки».</summary>
    public const string LocksKey = "Locks";

    /// <summary>Ключ вкладки «Рабочие процессы».</summary>
    public const string ProcessesKey = "Processes";

    /// <summary>
    /// Ключи вкладок в порядке их следования в окне (используется тестами для аудита).
    /// </summary>
    public static readonly IReadOnlyList<string> TabKeys = new[]
    {
        SessionsKey, ConnectionsKey, LocksKey, ProcessesKey
    };

    /// <summary>Средняя ширина одного символа заголовка, px (для базового шрифта 13).</summary>
    public const double CharWidthFactor = 7.5;

    /// <summary>
    /// Запас на отображение пиктограммы направления сортировки (▲/▼, 9 px + отступы
    /// 4/6 px) и горизонтальные отступы заголовка (ColumnHeaderStyle Padding="8,0"),
    /// px.
    /// </summary>
    public const double HeaderReserve = 40.0;

    /// <summary>Абсолютный минимум ширины колонки вкладки, px (служебные колонки-глифы).</summary>
    public const double AbsoluteMinWidth = 60.0;

    /// <summary>
    /// Минимальная ширина колонки, достаточная для заголовка и стрелки сортировки
    /// (issue #75): худший случай русской/английской локали. Для пустых заголовков
    /// (колонки-глифы «●») возвращается <see cref="AbsoluteMinWidth"/>.
    /// </summary>
    public static double HeaderMinWidth(string ru, string en)
    {
        var ruLen = string.IsNullOrEmpty(ru) ? 0 : ru.Length;
        var enLen = string.IsNullOrEmpty(en) ? 0 : en.Length;
        var maxLen = Math.Max(ruLen, enLen);
        if (maxLen == 0)
            return AbsoluteMinWidth;
        return Math.Max(AbsoluteMinWidth, Math.Ceiling(maxLen * CharWidthFactor) + HeaderReserve);
    }

    /// <summary>
    /// Нормализует сохранённые ширины перед применением (issue #75): некорректные
    /// значения (NaN/бесконечность/≤ 0) заменяются дефолтом колонки; корректные —
    /// ограничиваются снизу минимальной шириной колонки (заголовок + стрелка).
    /// Дополнительные значения за пределами числа колонок игнорируются.
    /// </summary>
    /// <param name="saved">Сохранённые ширины (null — запись отсутствует).</param>
    /// <param name="defaults">Дефолтные ширины колонок вкладки (для несохранённых позиций).</param>
    /// <param name="minWidths">Минимальные ширины колонок вкладки (issue #75).</param>
    /// <returns>Нормализованный массив ширины длиной <paramref name="defaults"/>.</returns>
    public static double[] ResolveWidths(
        IReadOnlyList<double>? saved,
        IReadOnlyList<double> defaults,
        IReadOnlyList<double> minWidths)
    {
        if (defaults is null)
            throw new ArgumentNullException(nameof(defaults));
        if (minWidths is not null && minWidths.Count != defaults.Count)
            throw new ArgumentException("Количество минимальных ширин должно совпадать с числом колонок.", nameof(minWidths));

        var result = new double[defaults.Count];
        for (var i = 0; i < defaults.Count; i++)
        {
            var min = minWidths is { Count: > 0 } ? minWidths[i] : AbsoluteMinWidth;
            var value = defaults[i];
            if (saved is not null && i < saved.Count && IsValid(saved[i]))
                value = Math.Max(saved[i], min);
            result[i] = Math.Max(value, min);
        }
        return result;
    }

    /// <summary>Ширина корректна для сохранения/применения (не NaN, не бесконечность, не ≤ 0).</summary>
    public static bool IsValid(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value) && value > 0.5;

    /// <summary>
    /// Сохраняет ширины колонок вкладки в настройки приложения (issue #75):
    /// запись по ключу вкладки заменяется целиком, остальные вкладки не затрагиваются.
    /// Некорректные значения (0 для гибких колонок, NaN) записываются как 0 — значит
    /// «не сохранять эту колонку» (при применении используется дефолт/минимум).
    /// </summary>
    /// <param name="settings">Настройки приложения (не null; словарь создаётся при необходимости).</param>
    /// <param name="tabKey">Ключ вкладки (Sessions/Connections/Locks/Processes).</param>
    /// <param name="widths">Фактические ширины колонок слева направо.</param>
    public static void Save(AppSettings settings, string tabKey, IReadOnlyList<double> widths)
    {
        if (settings is null)
            throw new ArgumentNullException(nameof(settings));
        if (string.IsNullOrWhiteSpace(tabKey))
            throw new ArgumentNullException(nameof(tabKey));
        if (widths is null)
            throw new ArgumentNullException(nameof(widths));

        settings.ServerMonitorColumnWidths ??= new Dictionary<string, double[]>();
        var stored = new double[widths.Count];
        for (var i = 0; i < widths.Count; i++)
            stored[i] = IsValid(widths[i]) ? Math.Round(widths[i], 1) : 0.0;
        settings.ServerMonitorColumnWidths[tabKey] = stored;
    }

    /// <summary>
    /// Возвращает сохранённые ширины вкладки (issue #75). Отсутствие записи —
    /// возвращается <c>null</c> (вызывающий использует дефолтные ширины по умолчанию).
    /// </summary>
    /// <param name="settings">Настройки приложения (null или без записи — null).</param>
    /// <param name="tabKey">Ключ вкладки (Sessions/Connections/Locks/Processes).</param>
    public static double[]? TryLoad(AppSettings? settings, string tabKey)
    {
        if (settings is null || string.IsNullOrWhiteSpace(tabKey))
            return null;
        if (settings.ServerMonitorColumnWidths is null)
            return null;
        return settings.ServerMonitorColumnWidths.TryGetValue(tabKey, out var widths) ? widths : null;
    }
}
