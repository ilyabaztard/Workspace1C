using System.Globalization;

namespace Workspace1C.Services.TechLogParser;

/// <summary>
/// Сборка CSV-отчёта по результатам анализа технологического журнала
/// (выпуск 1.0.0.86, п.4). Каждая секция (топы CALL/DBMSSQL/SDBL, блокировки,
/// исключения) — отдельный блок строк с заголовком колонок; секции разделены
/// пустой строкой. Файл получается через <see cref="CsvExporter"/>.
/// Заголовки колонок фиксированы (машиночитаемый формат, не локализуется).
/// </summary>
public static class TechLogCsvBuilder
{
    private static readonly string[] TopHeaders = { "Text", "Count", "MaxDurationMs", "AvgDurationMs", "TotalDurationMs" };
    private static readonly string[] LockHeaders = { "EventType", "Key", "Count", "TotalDurationMs", "MaxDurationMs", "LastSeen" };
    private static readonly string[] ExceptionHeaders = { "Key", "Count", "LastSeen", "FirstLine", "Example" };

    /// <summary>Строки CSV-документа (заголовок отчёта, секции, данные).</summary>
    public static IEnumerable<IReadOnlyList<string?>> BuildRows(TechLogAnalysisResult result, string? sourceInfo = null)
    {
        if (result is null)
            yield break;

        yield return new[] { "Workspace 1C — TechLog analysis" + (string.IsNullOrWhiteSpace(sourceInfo) ? string.Empty : $" ({sourceInfo})") };
        yield return new[]
        {
            $"Events: {result.TotalEvents}",
            $"CALL: {result.CountCalls}",
            $"DB: {result.CountSql}",
            $"SDBL: {result.CountSdbl}",
            $"TLOCK: {result.CountLocks}",
            $"TDEADLOCK: {result.CountDeadlocks}",
            $"EXCP: {result.CountExceptions}"
        };
        yield return Array.Empty<string?>();

        yield return new[] { "Top CALL" };
        yield return TopHeaders;
        foreach (var row in result.TopCalls)
            yield return TopRow(row);
        yield return Array.Empty<string?>();

        yield return new[] { "Top DBMSSQL (DB*)" };
        yield return TopHeaders;
        foreach (var row in result.TopSql)
            yield return TopRow(row);
        yield return Array.Empty<string?>();

        yield return new[] { "Top SDBL" };
        yield return TopHeaders;
        foreach (var row in result.TopSdbl)
            yield return TopRow(row);
        yield return Array.Empty<string?>();

        yield return new[] { "Locks (TLOCK/TDEADLOCK)" };
        yield return LockHeaders;
        foreach (var row in result.Locks)
            yield return new[]
            {
                row.EventType, row.Key, row.Count.ToString(CultureInfo.InvariantCulture),
                row.TotalDurationMs.ToString("0.###", CultureInfo.InvariantCulture),
                row.MaxDurationMs.ToString("0.###", CultureInfo.InvariantCulture),
                row.LastSeenText
            };
        yield return Array.Empty<string?>();

        yield return new[] { "Exceptions (EXCP)" };
        yield return ExceptionHeaders;
        foreach (var row in result.Exceptions)
            yield return new[]
            {
                row.Key, row.Count.ToString(CultureInfo.InvariantCulture),
                row.LastSeenText, row.FirstContextLine, row.ExampleContext
            };
    }

    /// <summary>Полный CSV-документ (UTF-8 в <see cref="CsvExporter.BuildDocument"/>).</summary>
    public static string BuildDocument(TechLogAnalysisResult result, string? sourceInfo = null)
        => CsvExporter.BuildDocument(BuildRows(result, sourceInfo));

    private static IReadOnlyList<string?> TopRow(TopOperationRow row) => new[]
    {
        row.Text,
        row.Count.ToString(CultureInfo.InvariantCulture),
        row.MaxDurationMs.ToString("0.###", CultureInfo.InvariantCulture),
        row.AvgDurationMs.ToString("0.###", CultureInfo.InvariantCulture),
        row.TotalDurationMs.ToString("0.###", CultureInfo.InvariantCulture)
    };
}