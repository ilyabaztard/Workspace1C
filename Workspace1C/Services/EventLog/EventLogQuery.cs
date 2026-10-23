using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Workspace1C.Models;

namespace Workspace1C.Services.EventLog;

/// <summary>
/// Условия отбора записей журнала регистрации (выпуск 1.0.0.69): период,
/// уровни важности и подстроки по пользователю, событию и тексту.
/// Пустая подстрока означает «без отбора».
/// </summary>
public sealed class EventLogQuery
{
    /// <summary>Лимит записей по умолчанию: больше в таблице читать неудобно.</summary>
    public const int DefaultMaxRows = 5000;

    public DateTime From { get; set; } = DateTime.Today;

    public DateTime To { get; set; } = DateTime.MaxValue;

    public bool IncludeInfo { get; set; } = true;

    public bool IncludeWarning { get; set; } = true;

    public bool IncludeError { get; set; } = true;

    public bool IncludeNote { get; set; } = true;

    public string User { get; set; } = string.Empty;

    public string Event { get; set; } = string.Empty;

    /// <summary>Подстрока в комментарии, данных, представлении данных или метаданных.</summary>
    public string Text { get; set; } = string.Empty;

    public int MaxRows { get; set; } = DefaultMaxRows;

    /// <summary>Проходит ли запись условия отбора (период тоже проверяется).</summary>
    public bool Matches(EventLogEntry entry)
    {
        if (entry is null)
            return false;
        if (entry.Timestamp < From || entry.Timestamp > To)
            return false;

        var severityOk = entry.Severity switch
        {
            EventLogSeverity.Warning => IncludeWarning,
            EventLogSeverity.Error => IncludeError,
            EventLogSeverity.Note => IncludeNote,
            _ => IncludeInfo,
        };
        if (!severityOk)
            return false;

        if (!ContainsIgnoreCase(entry.User, User))
            return false;

        if (!string.IsNullOrWhiteSpace(Event)
            && !ContainsIgnoreCase(entry.EventName, Event)
            && !ContainsIgnoreCase(EventLogPresentation.EventTitle(entry.EventName), Event))
            return false;

        if (!string.IsNullOrWhiteSpace(Text)
            && !ContainsIgnoreCase(entry.Comment, Text)
            && !ContainsIgnoreCase(entry.Data, Text)
            && !ContainsIgnoreCase(entry.DataPresentation, Text)
            && !ContainsIgnoreCase(entry.MetadataName, Text)
            && !ContainsIgnoreCase(entry.Computer, Text)
            && !ContainsIgnoreCase(entry.Application, Text))
            return false;

        return true;
    }

    private static bool ContainsIgnoreCase(string? value, string? needle)
    {
        if (string.IsNullOrWhiteSpace(needle))
            return true;
        return (value ?? string.Empty).IndexOf(needle.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
    }
}

/// <summary>Результат выборки журнала регистрации.</summary>
public sealed class EventLogQueryResult
{
    public IReadOnlyList<EventLogEntry> Entries { get; init; } = Array.Empty<EventLogEntry>();

    /// <summary>Выборка обрезана по лимиту <see cref="EventLogQuery.MaxRows"/>.</summary>
    public bool Truncated { get; init; }

    /// <summary>Ошибка чтения (null — успешно).</summary>
    public string? Error { get; init; }
}

/// <summary>
/// Единая точка чтения журнала регистрации (выпуск 1.0.0.69): по формату
/// расположения выбирает ридер (SQLite или последовательный), применяет отбор
/// и возвращает записи от новых к старым с лимитом. Чистая логика без UI —
/// используется окном журнала на обеих платформах.
/// </summary>
public static class EventLogQueryService
{
    public static EventLogQueryResult Query(JournalLocation location, EventLogQuery query, CancellationToken ct = default)
    {
        if (location is null || !location.IsValid)
        {
            return new EventLogQueryResult
            {
                Error = location?.ErrorMessage ?? "Журнал регистрации не найден."
            };
        }

        var max = query.MaxRows > 0 ? query.MaxRows : EventLogQuery.DefaultMaxRows;
        var result = new List<EventLogEntry>();
        var truncated = false;

        try
        {
            IEnumerable<EventLogEntry> source = location.Format switch
            {
                LgdFormat.Sqlite => new SqliteLgdReader().ReadNewestFirst(location, query.From, query.To, ct),
                LgdFormat.Sequential => new SequentialLgfReader().ReadNewestFirst(location, query.From, query.To, ct),
                _ => Array.Empty<EventLogEntry>(),
            };

            foreach (var entry in source)
            {
                ct.ThrowIfCancellationRequested();
                if (!query.Matches(entry))
                    continue;
                if (result.Count >= max)
                {
                    truncated = true;
                    break;
                }
                result.Add(entry);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new EventLogQueryResult
            {
                Entries = result,
                Truncated = truncated,
                Error = ex.Message
            };
        }

        return new EventLogQueryResult { Entries = result, Truncated = truncated };
    }

    /// <summary>
    /// Число ошибок за период (для сводок и уведомлений). Читает журнал с отбором
    /// «только ошибки»; при недоступном журнале возвращает null.
    /// </summary>
    public static int? CountErrors(JournalLocation location, DateTime from, DateTime to, int limit = 10000,
        CancellationToken ct = default)
    {
        if (location is null || !location.IsValid)
            return null;
        var query = new EventLogQuery
        {
            From = from,
            To = to,
            IncludeInfo = false,
            IncludeWarning = false,
            IncludeNote = false,
            IncludeError = true,
            MaxRows = limit
        };
        var result = Query(location, query, ct);
        return result.Error is null ? result.Entries.Count : null;
    }
}

/// <summary>Экспорт записей журнала в CSV (разделитель «;», UTF-8 с BOM — открывается в Excel).</summary>
public static class EventLogCsvExporter
{
    public static void Export(string path, IEnumerable<EventLogEntry> entries)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        writer.WriteLine(string.Join(";", new[]
        {
            "EventLog.Col.Date", "EventLog.Col.Severity", "EventLog.Col.User", "EventLog.Col.Computer",
            "EventLog.Col.Application", "EventLog.Col.Event", "EventLog.Col.Metadata", "EventLog.Col.Comment",
            "EventLog.Col.Data", "EventLog.Col.DataPresentation", "EventLog.Col.Transaction", "EventLog.Col.Session"
        }.Select(key => Escape(Workspace1C.Localization.LocalizationManager.T(key)))));
        foreach (var e in entries)
        {
            writer.WriteLine(string.Join(";", new[]
            {
                e.Timestamp.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture),
                EventLogPresentation.SeverityTitle(e.Severity),
                e.User,
                e.Computer,
                EventLogPresentation.ApplicationTitle(e.Application),
                EventLogPresentation.EventTitle(e.EventName),
                e.MetadataName,
                e.Comment,
                e.Data,
                e.DataPresentation,
                EventLogPresentation.TransactionTitle(e.TransactionStatus),
                e.Session == 0 ? string.Empty : e.Session.ToString(CultureInfo.InvariantCulture)
            }.Select(Escape)));
        }
    }

    public static string Escape(string? value)
    {
        var text = value ?? string.Empty;
        if (text.IndexOfAny(new[] { ';', '"', '\r', '\n' }) < 0)
            return text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
