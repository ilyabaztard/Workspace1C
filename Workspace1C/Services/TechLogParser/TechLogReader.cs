using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Workspace1C.Services.TechLogParser;

/// <summary>
/// Построчный разбор технологического журнала 1С (выпуск 1.0.0.86, п.4).
/// Устойчив к разным наборам свойств: обязателен только префикс даты/времени
/// <c>yyyyMMdd HHmmss[.mmm]-pid-tid</c>; пары <c>ключ=значение</c> разбираются до
/// токена <c>event=…</c>, весь остаток строки — контекст события. Строки без
/// префикса даты продолжают контекст текущего события. Чистый класс без
/// UI-зависимостей — используется WPF и Avalonia, покрыт юнит-тестами.
/// </summary>
public static class TechLogReader
{
    /// <summary>
    /// Префикс строки события: дата, время (с миллисекундами или без) и
    /// идентификаторы процесса/потока после дефиса.
    /// </summary>
    private static readonly Regex EventStart = new(
        @"^(?<ts>\d{4}\d{2}\d{2}[ ]\d{2}\d{2}\d{2}(?:\.\d{1,7})?)-\d+-\d+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Токен «свойство=значение».</summary>
    private static readonly Regex KeyValue = new(
        @"^(?<key>[A-Za-z_][\w:.\-]*)=(?<value>\S+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Ключи, по которым определяется имя информационной базы.
    private static readonly string[] BaseKeys =
    {
        "Base", "InfoBase", "Infobase", "baseName", "dbBaseName", "DBBaseName", "p:base", "InfobaseName"
    };

    // Имена процессов платформы — не являются именем базы.
    private static readonly HashSet<string> KnownProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "1cv8", "1cv8c", "1cv8s", "1cv8rd", "ragent", "rmngr", "rphost", "dbsrvr", "crash"
    };

    /// <summary>
    /// Обходит каталог логов: все файлы <c>*.log</c>, включая подкаталоги.
    /// Несуществующий каталог — пустой список; недоступный файл пропускается.
    /// </summary>
    public static IReadOnlyList<TechLogEvent> ReadDirectory(
        string? logDirectory, TechLogQuery? query = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(logDirectory) || !Directory.Exists(logDirectory))
            return Array.Empty<TechLogEvent>();

        var events = new List<TechLogEvent>();
        foreach (var file in Directory.EnumerateFiles(logDirectory, "*.log", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                ParseStream(reader, events, query, cancellationToken);
            }
            catch (IOException)
            {
                // Файл занят или удалён платформой во время чтения — пропускаем.
            }
            catch (UnauthorizedAccessException)
            {
                // Нет прав на файл — пропускаем.
            }
        }

        return events;
    }

    /// <summary>Разбирает текст журнала (для тестов и встроенных небольших логов).</summary>
    public static IReadOnlyList<TechLogEvent> ParseEventsText(string? text, TechLogQuery? query = null)
    {
        var events = new List<TechLogEvent>();
        using var reader = new StringReader(text ?? string.Empty);
        ParseStream(reader, events, query);
        return events;
    }

    /// <summary>
    /// Построчно разбирает поток: строка с префиксом даты начинает новое событие,
    /// остальные — продолжают контекст текущего. События, не прошедшие
    /// <paramref name="query"/>, в результат не добавляются.
    /// </summary>
    public static void ParseStream(
        TextReader reader, ICollection<TechLogEvent> output, TechLogQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        if (reader is null || output is null)
            return;

        PendingEvent? pending = null;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var match = EventStart.Match(line);
            if (match.Success)
            {
                Flush(pending, output, query);
                pending = ParseFirstLine(line, match);
                continue;
            }

            if (pending is not null)
                pending.ContextLines.Add(line);
        }

        Flush(pending, output, query);
    }

    private static PendingEvent? ParseFirstLine(string line, Match match)
    {
        var ts = ParseTimestamp(match.Groups["ts"].Value);
        if (ts is null)
            return null;

        var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var rest = line.Substring(match.Index + match.Length);
        var tokens = rest.Split(' ');

        string? eventType = null;
        var context = new StringBuilder();
        var inContext = false;

        foreach (var token in tokens)
        {
            if (token.Length == 0)
                continue;

            if (inContext)
            {
                AppendToken(context, token);
                continue;
            }

            if (token.StartsWith("event=", StringComparison.OrdinalIgnoreCase))
            {
                eventType = token.Substring("event=".Length);
                inContext = true;
                continue;
            }

            var kv = KeyValue.Match(token);
            if (kv.Success)
            {
                props[kv.Groups["key"].Value] = kv.Groups["value"].Value;
                continue;
            }

            // Токен без «=»: нестандартная строка — весь остаток считаем контекстом.
            inContext = true;
            AppendToken(context, token);
        }

        props.TryGetValue("usr", out var user);
        props.TryGetValue("process", out var process);
        props.TryGetValue("p:processName", out var processName);
        props.TryGetValue("duration", out var durationText);
        _ = long.TryParse(durationText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var durationUs);

        return new PendingEvent
        {
            Timestamp = ts.Value,
            EventType = eventType ?? string.Empty,
            Process = process ?? string.Empty,
            Base = ResolveBase(props, processName, process),
            User = user ?? string.Empty,
            DurationUs = durationUs,
            Properties = props,
            Context = context.ToString().TrimEnd(),
            ContextLines = []
        };
    }

    private static void Flush(PendingEvent? pending, ICollection<TechLogEvent> output, TechLogQuery? query)
    {
        if (pending is null)
            return;

        var fullContext = pending.Context;
        if (pending.ContextLines.Count > 0)
        {
            var sb = new StringBuilder(fullContext);
            foreach (var extra in pending.ContextLines)
            {
                sb.AppendLine();
                sb.Append(extra);
            }
            fullContext = sb.ToString();
        }

        var ev = new TechLogEvent
        {
            Timestamp = pending.Timestamp,
            EventType = pending.EventType,
            Process = pending.Process,
            Base = pending.Base,
            User = pending.User,
            DurationUs = pending.DurationUs,
            Context = fullContext,
            Properties = pending.Properties
        };

        if (query is null || query.Matches(ev))
            output.Add(ev);
    }

    private static void AppendToken(StringBuilder sb, string token)
    {
        if (sb.Length > 0)
            sb.Append(' ');
        sb.Append(token);
    }

    private static DateTime? ParseTimestamp(string value)
    {
        var dot = value.IndexOf('.');
        var datePart = dot >= 0 ? value.Substring(0, dot) : value;
        if (!DateTime.TryParseExact(datePart, "yyyyMMdd HHmmss", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
            return null;

        if (dot >= 0 && dot + 1 < value.Length &&
            int.TryParse(value.Substring(dot + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var frac))
        {
            // Дробная часть — доли секунды: нормализуем к тикам (100 нс).
            var digits = value.Length - dot - 1;
            var ticks = (long)Math.Round(frac * Math.Pow(10, 7 - digits));
            date = date.AddTicks(Math.Clamp(ticks, 0, TimeSpan.TicksPerSecond - 1));
        }

        return date;
    }

    /// <summary>
    /// Определяет информационную базу события: свойства базы в Properties,
    /// затем <c>p:processName</c> клиента (если это не процесс платформы) и
    /// часть <c>process</c> после разделителя <c>@</c>/<c>:</c>.
    /// </summary>
    internal static string ResolveBase(
        IReadOnlyDictionary<string, string> props, string? processName, string? process)
    {
        foreach (var key in BaseKeys)
        {
            if (props.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        if (!string.IsNullOrWhiteSpace(processName) && !KnownProcesses.Contains(processName))
            return processName.Trim();

        if (!string.IsNullOrWhiteSpace(process))
        {
            var separator = process.IndexOfAny(new[] { '@', ':' });
            if (separator >= 0 && separator + 1 < process.Length)
                return process.Substring(separator + 1).Trim();
        }

        return string.Empty;
    }

    private sealed class PendingEvent
    {
        public DateTime Timestamp { get; init; }
        public string EventType { get; init; } = string.Empty;
        public string Process { get; init; } = string.Empty;
        public string Base { get; init; } = string.Empty;
        public string User { get; init; } = string.Empty;
        public long DurationUs { get; init; }
        public Dictionary<string, string> Properties { get; init; } = null!;
        public string Context { get; init; } = string.Empty;
        public List<string> ContextLines { get; init; } = [];
    }
}