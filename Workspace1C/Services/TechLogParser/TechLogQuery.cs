namespace Workspace1C.Services.TechLogParser;

/// <summary>
/// Фильтры разбора/анализа технологического журнала (выпуск 1.0.0.86, п.4):
/// период, информационная база (контекст/процесс), пользователь и типы событий.
/// Порог длительности <see cref="MinDurationUs"/> применяется в анализаторе только
/// к «долгим» операциям (CALL/DBMSSQL/SDBL); блокировки и исключения проходят
/// всегда, иначе события TLOCK с нулевой длительностью ожидания были бы потеряны.
/// </summary>
public sealed class TechLogQuery
{
    /// <summary>Начало периода (включительно). null — без нижней границы.</summary>
    public DateTime? From { get; set; }

    /// <summary>Конец периода (включительно). null — без верхней границы.</summary>
    public DateTime? To { get; set; }

    /// <summary>
    /// Подстрока отбора по базе: ищется в <see cref="TechLogEvent.Base"/>,
    /// <see cref="TechLogEvent.Process"/> и контексте события (без учёта регистра).
    /// Вид «p:processName/контекст ИБ» из требований п.4.
    /// </summary>
    public string? BaseFilter { get; set; }

    /// <summary>Подстрока отбора по пользователю (<c>usr</c>, без учёта регистра).</summary>
    public string? UserFilter { get; set; }

    /// <summary>Минимальная длительность для «долгих» операций, микросекунды.</summary>
    public long MinDurationUs { get; set; }

    /// <summary>
    /// Допустимые типы событий (без учёта регистра). null/пустой набор — все события.
    /// </summary>
    public IReadOnlyCollection<string>? EventTypes { get; set; }

    private ISet<string>? _normalized;

    /// <summary>Признак «собирать все события» (пустые фильтры).</summary>
    public bool IsEmpty =>
        From is null && To is null &&
        string.IsNullOrWhiteSpace(BaseFilter) &&
        string.IsNullOrWhiteSpace(UserFilter) &&
        MinDurationUs <= 0 && EventTypes is null;

    /// <summary>Проверяет событие по периоду, базе, пользователю и типам (длительность — нет).</summary>
    public bool Matches(TechLogEvent e)
    {
        if (From is { } from && e.Timestamp < from)
            return false;
        if (To is { } to && e.Timestamp > to)
            return false;

        var filter = StringTrim(BaseFilter);
        if (filter.Length > 0 &&
            !Contains(e.Base, filter) && !Contains(e.Process, filter) && !Contains(e.Context, filter))
            return false;

        var user = StringTrim(UserFilter);
        if (user.Length > 0 && !Contains(e.User, user))
            return false;

        if (EventTypes is { Count: > 0 })
        {
            _normalized ??= new HashSet<string>(
                EventTypes.Select(t => t.Trim().ToUpperInvariant()), StringComparer.Ordinal);
            if (!_normalized.Contains(e.EventType.ToUpperInvariant()))
                return false;
        }

        return true;
    }

    private static string StringTrim(string? value) => (value ?? string.Empty).Trim();

    private static bool Contains(string haystack, string needle)
        => haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
}