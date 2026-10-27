using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Workspace1C.Services.TechLogParser;

/// <summary>Строка топа долгих операций: группа событий по тексту запроса/вызова.</summary>
public sealed class TopOperationRow
{
    public string Text { get; init; } = string.Empty;
    public long Count { get; init; }
    public double MaxDurationMs { get; init; }
    public double TotalDurationMs { get; init; }
    public double AvgDurationMs => Count > 0 ? TotalDurationMs / Count : 0;
    public string MaxMsText => MaxDurationMs.ToString("0.###", CultureInfo.CurrentCulture);
    public string AvgMsText => AvgDurationMs.ToString("0.###", CultureInfo.CurrentCulture);
    public string TotalMsText => TotalDurationMs.ToString("0.###", CultureInfo.CurrentCulture);
}

/// <summary>Строка сводки по управляемым блокировкам/взаимоблокировкам.</summary>
public sealed class LockSummaryRow
{
    public string EventType { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
    public long Count { get; init; }
    public double TotalDurationMs { get; init; }
    public double MaxDurationMs { get; init; }
    public DateTime LastSeen { get; init; }
    public string LastSeenText => LastSeen.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);
    public string TotalMsText => TotalDurationMs.ToString("0.###", CultureInfo.CurrentCulture);
    public string MaxMsText => MaxDurationMs.ToString("0.###", CultureInfo.CurrentCulture);
}

/// <summary>Сгруппированное исключение EXCP: нормализованный ключ без динамических частей.</summary>
public sealed class ExceptionGroupRow
{
    public string Key { get; init; } = string.Empty;
    public long Count { get; init; }
    public DateTime LastSeen { get; init; }
    public string LastSeenText => LastSeen.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);
    public string ExampleContext { get; init; } = string.Empty;
    public string FirstContextLine { get; init; } = string.Empty;
}

/// <summary>Результат анализа технологического журнала.</summary>
public sealed class TechLogAnalysisResult
{
    public IReadOnlyList<TopOperationRow> TopCalls { get; init; } = Array.Empty<TopOperationRow>();
    public IReadOnlyList<TopOperationRow> TopSql { get; init; } = Array.Empty<TopOperationRow>();
    public IReadOnlyList<TopOperationRow> TopSdbl { get; init; } = Array.Empty<TopOperationRow>();
    public IReadOnlyList<LockSummaryRow> Locks { get; init; } = Array.Empty<LockSummaryRow>();
    public IReadOnlyList<ExceptionGroupRow> Exceptions { get; init; } = Array.Empty<ExceptionGroupRow>();

    public long TotalEvents { get; init; }
    public long CountCalls { get; init; }
    public long CountSql { get; init; }
    public long CountSdbl { get; init; }
    public long CountLocks { get; init; }
    public long CountDeadlocks { get; init; }
    public long CountExceptions { get; init; }

    public DateTime? FirstSeen { get; init; }
    public DateTime? LastSeen { get; init; }
}

/// <summary>
/// Агрегация событий технологического журнала (выпуск 1.0.0.86, п.4):
/// топы долгих операций (CALL / DBMSSQL и др. DB* / SDBL), сводка блокировок
/// (TLOCK, TDEADLOCK) и группировка исключений EXCP по нормализованному контексту.
/// Чистый класс — без UI-зависимостей, покрыт тестами.
/// </summary>
public static class TechLogAnalyzer
{
    public const int DefaultTopCount = 100;

    /// <summary>Типы «долгих» событий СУБД, собираемых в топ запросов.</summary>
    private static readonly HashSet<string> DbEventTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "DBMSSQL", "DBPOSTGRS", "DBORACLE", "DB2", "DBV8DBENG"
    };

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex ObjectsInContext = new(
        @"\bObjects?\s*=\s*([^,\s;]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Строит сводку: фильтрует события по <paramref name="query"/> (период, база,
    /// пользователь, типы) и агрегирует топы/блокировки/исключения. Порог
    /// <see cref="TechLogQuery.MinDurationUs"/> применяется только к «долгим» операциям.
    /// </summary>
    public static TechLogAnalysisResult Analyze(
        IReadOnlyCollection<TechLogEvent> events, TechLogQuery? query = null,
        int topCount = DefaultTopCount)
    {
        var filtered = (query is null ? events : events.Where(query.Matches)).ToList();
        var minDuration = query?.MinDurationUs ?? 0;

        var topCalls = BuildTop(filtered, e => IsEvent(e, "CALL"), minDuration, topCount);
        var topSql = BuildTop(filtered, e => DbEventTypes.Contains(e.EventType), minDuration, topCount);
        var topSdbl = BuildTop(filtered, e => IsEvent(e, "SDBL"), minDuration, topCount);
        var locks = BuildLocks(filtered);
        var exceptions = BuildExceptions(filtered);

        return new TechLogAnalysisResult
        {
            TopCalls = topCalls,
            TopSql = topSql,
            TopSdbl = topSdbl,
            Locks = locks,
            Exceptions = exceptions,
            TotalEvents = filtered.Count,
            CountCalls = filtered.Count(e => IsEvent(e, "CALL")),
            CountSql = filtered.Count(e => DbEventTypes.Contains(e.EventType)),
            CountSdbl = filtered.Count(e => IsEvent(e, "SDBL")),
            CountLocks = filtered.Count(e => IsEvent(e, "TLOCK")),
            CountDeadlocks = filtered.Count(e => IsEvent(e, "TDEADLOCK")),
            CountExceptions = filtered.Count(e => IsEvent(e, "EXCP")),
            FirstSeen = filtered.Count > 0 ? filtered.Min(e => e.Timestamp) : null,
            LastSeen = filtered.Count > 0 ? filtered.Max(e => e.Timestamp) : null
        };
    }

    /// <summary>
    /// Нормализует контекст исключения: убирает числа/длительности, времена,
    /// длинные хеши и имена временных таблиц, схлопывает пробелы. Одинаковые
    /// исключения с разными динамическими данными дают один ключ группы.
    /// </summary>
    public static string NormalizeExceptionContext(string? context)
    {
        var s = context ?? string.Empty;
        // Порядок важен: временные таблицы (#tmp123) и времена (12:34:56) убираем
        // ДО замены чисел, иначе «123» и «12» уже не дадут собрать группу.
        s = TempTables.Replace(s, "<TMP>");
        s = TimePatterns.Replace(s, "<T>");
        s = HexTokens.Replace(s, "<HEX>");
        s = Numbers.Replace(s, "<N>");
        s = Whitespace.Replace(s, " ").Trim();
        return s.Length == 0 ? FirstTextLine(context).Trim() : s;
    }

    private static readonly Regex Numbers = new(@"[+-]?\d+(?:[.,]\d+)?%?", RegexOptions.Compiled);
    private static readonly Regex TimePatterns = new(@"\b\d{1,2}:\d{2}(?::\d{2})?\b", RegexOptions.Compiled);
    private static readonly Regex TempTables = new(@"#\w+", RegexOptions.Compiled);
    private static readonly Regex HexTokens = new(@"\b[0-9a-fA-F]{8,}\b", RegexOptions.Compiled);

    private static IReadOnlyList<TopOperationRow> BuildTop(
        IEnumerable<TechLogEvent> source, Func<TechLogEvent, bool> match, long minDurationUs, int topCount)
    {
        return source
            .Where(e => match(e) && e.DurationUs >= minDurationUs)
            .GroupBy(e => CleanText(e.Context), StringComparer.OrdinalIgnoreCase)
            .Select(g => new TopOperationRow
            {
                Text = g.Key,
                Count = g.Count(),
                MaxDurationMs = g.Max(e => e.DurationUs) / 1000.0,
                TotalDurationMs = g.Sum(e => e.DurationUs) / 1000.0
            })
            .OrderByDescending(r => r.TotalDurationMs)
            .ThenByDescending(r => r.MaxDurationMs)
            .Take(topCount)
            .ToList();
    }

    private static IReadOnlyList<LockSummaryRow> BuildLocks(IEnumerable<TechLogEvent> source)
    {
        return source
            .Where(e => IsEvent(e, "TLOCK") || IsEvent(e, "TDEADLOCK"))
            .GroupBy(e => e.EventType.ToUpperInvariant() + "|" + ResolveLockKey(e), StringComparer.Ordinal)
            .Select(g => new LockSummaryRow
            {
                EventType = g.Key.Split('|')[0],
                Key = g.Key.Substring(g.Key.IndexOf('|') + 1),
                Count = g.Count(),
                TotalDurationMs = g.Sum(e => e.DurationUs) / 1000.0,
                MaxDurationMs = g.Max(e => e.DurationUs) / 1000.0,
                LastSeen = g.Max(e => e.Timestamp)
            })
            .OrderByDescending(r => r.EventType)
            .ThenByDescending(r => r.Count)
            .ToList();
    }

    private static IReadOnlyList<ExceptionGroupRow> BuildExceptions(IEnumerable<TechLogEvent> source)
    {
        var groups = source
            .Where(e => IsEvent(e, "EXCP"))
            .GroupBy(e => NormalizeExceptionContext(e.Context), StringComparer.Ordinal);

        var result = new List<ExceptionGroupRow>();
        foreach (var group in groups)
        {
            var example = group.Last().Context;
            result.Add(new ExceptionGroupRow
            {
                Key = group.Key,
                Count = group.Count(),
                LastSeen = group.Max(e => e.Timestamp),
                ExampleContext = example,
                FirstContextLine = FirstTextLine(example)
            });
        }

        return result
            .OrderByDescending(r => r.Count)
            .ThenByDescending(r => r.LastSeen)
            .ToList();
    }

    private static bool IsEvent(TechLogEvent e, string type)
        => e.EventType.Equals(type, StringComparison.OrdinalIgnoreCase);

    /// <summary>Один читаемый текст запроса/вызова: без переносов и префикса «ТИП:».</summary>
    internal static string CleanText(string? context)
    {
        var s = (context ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        foreach (var prefix in new[] { "CALL:", "DBMSSQL:", "SDBL:", "EXCP:" })
        {
            if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                s = s.Substring(prefix.Length).TrimStart();
                break;
            }
        }
        return Whitespace.Replace(s, " ").Trim();
    }

    /// <summary>Первая непустая строка текста (пример контекста для таблиц).</summary>
    internal static string FirstTextLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
                return trimmed.Length > 200 ? trimmed.Substring(0, 200) : trimmed;
        }
        return string.Empty;
    }

    /// <summary>
    /// Ключ группы блокировки: свойство <c>Objects</c> (или <c>Objects=…</c> в
    /// контексте), иначе — первая значимая строка контекста события.
    /// </summary>
    internal static string ResolveLockKey(TechLogEvent e)
    {
        foreach (var key in new[] { "Objects", "Object", "LockObjects", "LockObject" })
        {
            if (e.Properties.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        var match = ObjectsInContext.Match(e.Context);
        if (match.Success)
            return match.Groups[1].Value.Trim();

        var line = FirstTextLine(e.Context);
        return line.Length > 160 ? line.Substring(0, 160) : line;
    }
}