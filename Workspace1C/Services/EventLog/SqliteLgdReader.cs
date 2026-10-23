using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Workspace1C.Models;
using Microsoft.Data.Sqlite;

namespace Workspace1C.Services.EventLog;

/// <summary>
/// Чтение журнала регистрации в формате SQLite (файл 1Cv8.lgd; платформы
/// 8.3.5–8.3.21). Файл — стандартная база данных SQLite 3: основная таблица
/// eventLog и справочники кодов (userCodes, computerCodes, appCodes,
/// eventCodes, metadataCodes).
///
/// Открывается строго read-only (Mode=ReadOnly), чтобы не создавать -shm/-wal
/// рядом с файлом и не мешать платформе 1С: долгие чтения живого SQLite-журнала
/// блокируют запись событий (известная проблема формата, см. «Записки
/// оптимизатора 1С», ч.13). Окно просмотра дополнительно предупреждает о
/// запущенной базе.
///
/// Имена колонок не жёстко зафиксированы: схема 1С отличалась между версиями
/// платформы, поэтому выполняется интроспекция (PRAGMA table_info) и маппинг
/// по известным псевдонимам; отсутствующие колонки и справочники пропускаются
/// (fallback на «сырые» коды вида «#12»).
/// </summary>
public sealed class SqliteLgdReader : ILgdReader
{
    /// <summary>Кандидаты на имя основной таблицы событий.</summary>
    private static readonly string[] EventTableCandidates = { "eventLog", "event_log", "EventLog" };

    private static readonly string[] UserTableCandidates = { "userCodes", "userNames", "users" };
    private static readonly string[] ComputerTableCandidates = { "computerCodes", "computerNames", "computers" };
    private static readonly string[] AppTableCandidates = { "appCodes", "apps" };
    private static readonly string[] EventCodeTableCandidates = { "eventCodes", "events" };
    private static readonly string[] MetadataTableCandidates = { "metadataCodes", "metadata" };

    /// <inheritdoc />
    public LgdFormat Format => LgdFormat.Sqlite;

    /// <inheritdoc />
    public bool CanRead(JournalLocation location) =>
        location is not null
        && location.Format == LgdFormat.Sqlite
        && !string.IsNullOrEmpty(location.MainFile)
        && File.Exists(location.MainFile);

    /// <inheritdoc />
    public long CountRows(JournalLocation location, CancellationToken ct = default)
    {
        EnsureCanRead(location);
        using var conn = Open(location.MainFile);
        var eventTable = FindTable(conn, EventTableCandidates);
        if (eventTable is null)
            return 0;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM \"{eventTable}\"";
        return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public IEnumerable<EventLogEntry> ReadPage(
        JournalLocation location,
        int skip,
        int take,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        EnsureCanRead(location);
        if (skip < 0)
            throw new ArgumentOutOfRangeException(nameof(skip));
        if (take <= 0)
            return Array.Empty<EventLogEntry>();

        return ReadRows(location, skip, take, null, null, newestFirst: false, progress, ct);
    }

    /// <summary>
    /// Записи за период [<paramref name="from"/>; <paramref name="to"/>] от новых к старым
    /// (выпуск 1.0.0.69): отбор по дате выполняет SQLite, поэтому большой журнал
    /// не читается целиком.
    /// </summary>
    public IEnumerable<EventLogEntry> ReadNewestFirst(
        JournalLocation location, DateTime from, DateTime to, CancellationToken ct = default)
    {
        EnsureCanRead(location);
        return ReadRows(location, 0, -1, ToPlatformDate(from), ToPlatformDate(to), newestFirst: true, null, ct);
    }

    /// <summary>Обратное к <see cref="FromPlatformDate"/> преобразование (для условий отбора).</summary>
    public static long ToPlatformDate(DateTime value)
    {
        if (value <= DateTime.MinValue)
            return 0;
        if (value >= DateTime.MaxValue)
            return long.MaxValue;
        return value.Ticks / 1000L;
    }

    private IEnumerable<EventLogEntry> ReadRows(
        JournalLocation location,
        int skip,
        int take,
        long? fromValue,
        long? toValue,
        bool newestFirst,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        using var conn = Open(location.MainFile);
        var eventTable = FindTable(conn, EventTableCandidates);
        if (eventTable is null)
            yield break;

        var cols = ReadTableColumns(conn, eventTable);

        // Роли колонок. Без колонки даты читать журнал бессмысленно — прекращаем.
        var dateIdx = cols.FindIndex("date", "dt", "tm");
        if (dateIdx < 0)
            yield break;
        var tranIdx = cols.FindIndex("transactionstatus", "tran");
        var userIdx = cols.FindIndex("usercode", "usr");
        var computerIdx = cols.FindIndex("computercode", "com");
        var appIdx = cols.FindIndex("appcode", "app");
        var eventIdx = cols.FindIndex("eventcode", "evt", "event");
        // В реальных файлах 1Cv8.lgd колонки называются «severity» и «metadataCodes»
        // (1.0.0.69); прежние псевдонимы оставлены для совместимости.
        var sevIdx = cols.FindIndex("severitycode", "sev", "severity");
        var commentIdx = cols.FindIndex("comment");
        var metaCodeIdx = cols.FindIndex("metadatacode", "mdcode", "metadatacodes");
        var dataIdx = cols.FindIndex("data", "dta");
        var dataPresentationIdx = cols.FindIndex("datapresentation");
        var sessionIdx = cols.FindIndex("session");

        // Справочники кодов (могут отсутствовать — тогда используем «#код»).
        var userNames = ReadCodeNames(conn, UserTableCandidates);
        var computerNames = ReadCodeNames(conn, ComputerTableCandidates);
        var appNames = ReadCodeNames(conn, AppTableCandidates);
        var eventNames = ReadCodeNames(conn, EventCodeTableCandidates);
        var metadata = ReadMetadata(conn, MetadataTableCandidates);

        // Строим SELECT из нужных колонок в порядке их следования в таблице.
        var needed = new List<(int Index, string Role)>();
        void Need(int index, string role)
        {
            if (index >= 0)
                needed.Add((index, role));
        }

        Need(dateIdx, "date");
        Need(tranIdx, "tran");
        Need(userIdx, "user");
        Need(computerIdx, "computer");
        Need(appIdx, "app");
        Need(eventIdx, "event");
        Need(sevIdx, "sev");
        Need(commentIdx, "comment");
        Need(metaCodeIdx, "metadata");
        Need(dataIdx, "data");
        Need(dataPresentationIdx, "dataPresentation");
        Need(sessionIdx, "session");

        if (needed.Count == 0)
            yield break;

        var selectList = string.Join(", ", needed.Select(n => $"\"{cols.Order[n.Index]}\""));
        var dateColumn = cols.Order[dateIdx];
        using var cmd = conn.CreateCommand();
        var where = string.Empty;
        if (fromValue is not null)
        {
            where = $" WHERE \"{dateColumn}\" >= @from AND \"{dateColumn}\" <= @to";
            cmd.Parameters.AddWithValue("@from", fromValue.Value);
            cmd.Parameters.AddWithValue("@to", toValue ?? long.MaxValue);
        }
        var order = newestFirst ? "DESC" : "ASC";
        var limit = take > 0 ? " LIMIT @take OFFSET @skip" : string.Empty;
        cmd.CommandText = $"SELECT rowid, {selectList} FROM \"{eventTable}\"{where} ORDER BY rowid {order}{limit}";
        if (take > 0)
        {
            cmd.Parameters.AddWithValue("@take", take);
            cmd.Parameters.AddWithValue("@skip", skip);
        }

        var roleIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < needed.Count; i++)
            roleIndex[needed[i].Role] = i + 1; // +1, т.к. 0 — это rowid

        using var reader = cmd.ExecuteReader();
        var emitted = 0;
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            yield return MapRow(reader, roleIndex, location.MainFile,
                userNames, computerNames, appNames, eventNames, metadata);

            emitted++;
            if (progress is not null && take > 0)
                progress.Report((double)emitted / take);
        }
    }

    /// <summary>
    /// Преобразует целочисленную дату платформы в <see cref="DateTime"/>.
    /// По разбору сообщества значение хранится в интервалах 1/10000 секунды
    /// (100 мкс) от 01.01.0001; эквивалент <c>new DateTime(value * 1000)</c>,
    /// где Ticks — интервалы по 100 нс. Формула зафиксирована юнит-тестами на
    /// фикстуре; при наличии реального образца .lgd сверить (приёмка этапа).
    /// Невалидные значения дают <see cref="DateTime.MinValue"/>.
    /// </summary>
    public static DateTime FromPlatformDate(long value)
    {
        if (value <= 0 || value > DateTime.MaxValue.Ticks / 1000L)
            return DateTime.MinValue;
        return new DateTime(value * 1000L, DateTimeKind.Local);
    }

    private void EnsureCanRead(JournalLocation location)
    {
        if (!CanRead(location))
            throw new InvalidOperationException(
                $"Ридер {nameof(SqliteLgdReader)} не может прочитать расположение журнала " +
                $"(формат: {location?.Format ?? LgdFormat.Unknown}).");
    }

    private static SqliteConnection Open(string path)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
        };

        var conn = new SqliteConnection(builder.ToString());
        try
        {
            conn.Open();

            // Ошибка «file is not a database» возникает на первом запросе, а не при
            // открытии — выполняем контрольный запрос сразу, чтобы отдать понятную
            // InvalidDataException вместо сырой SqliteException.
            using (var check = conn.CreateCommand())
            {
                check.CommandText = "SELECT name FROM sqlite_master LIMIT 1";
                check.ExecuteScalar();
            }

            return conn;
        }
        catch (SqliteException ex)
        {
            conn.Dispose();
            throw new InvalidDataException($"Файл журнала не является базой данных SQLite: {path}", ex);
        }
    }

    private static string? FindTable(SqliteConnection conn, IEnumerable<string> candidates)
    {
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                tables.Add(r.GetString(0));
        }

        return candidates.FirstOrDefault(t => tables.Contains(t));
    }

    /// <summary>
    /// Колонки таблицы: имя → порядковый номер плюс список имён в порядке
    /// следования (для построения SELECT).
    /// </summary>
    private sealed class TableColumns
    {
        public required Dictionary<string, int> ByName { get; init; }
        public required List<string> Order { get; init; }

        public int FindIndex(params string[] aliases)
        {
            foreach (var alias in aliases)
            {
                if (ByName.TryGetValue(alias, out var index))
                    return index;
            }
            return -1;
        }
    }

    private static TableColumns ReadTableColumns(SqliteConnection conn, string table)
    {
        var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\")";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            // Столбцы PRAGMA table_info: cid, name, type, notnull, dflt_value, pk.
            var name = r.GetString(1);
            var ordinal = r.GetInt32(0);
            byName[name] = ordinal;
            order.Add(name);
        }

        return new TableColumns { ByName = byName, Order = order };
    }

    private static Dictionary<long, string> ReadCodeNames(SqliteConnection conn, IEnumerable<string> candidates)
    {
        var table = FindTable(conn, candidates);
        if (table is null)
            return new Dictionary<long, string>();

        try
        {
            var cols = ReadTableColumns(conn, table);
            var codeIdx = cols.FindIndex("code", "id", "key");
            var nameIdx = cols.FindIndex("name", "title", "description");
            if (codeIdx < 0 || nameIdx < 0)
                return new Dictionary<long, string>();

            var result = new Dictionary<long, string>();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT \"{cols.Order[codeIdx]}\", \"{cols.Order[nameIdx]}\" FROM \"{table}\"";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                result[r.GetInt64(0)] = r.GetString(1);
            return result;
        }
        catch (SqliteException)
        {
            // Справочник непредвиденной структуры — не мешаем чтению событий.
            return new Dictionary<long, string>();
        }
    }

    /// <summary>Метаданные: код → (тип, имя). Оба поля могут отсутствовать в словаре.</summary>
    private static Dictionary<long, (string Name, string Type)> ReadMetadata(SqliteConnection conn, IEnumerable<string> candidates)
    {
        var table = FindTable(conn, candidates);
        if (table is null)
            return new Dictionary<long, (string, string)>();

        try
        {
            var cols = ReadTableColumns(conn, table);
            var codeIdx = cols.FindIndex("code", "id", "key");
            var nameIdx = cols.FindIndex("name", "title");
            var typeIdx = cols.FindIndex("type", "typename", "mdtype");
            if (codeIdx < 0 || (nameIdx < 0 && typeIdx < 0))
                return new Dictionary<long, (string, string)>();

            var result = new Dictionary<long, (string, string)>();
            using var cmd = conn.CreateCommand();
            var selects = new List<string> { $"\"{cols.Order[codeIdx]}\"" };
            if (nameIdx >= 0)
                selects.Add($"\"{cols.Order[nameIdx]}\"");
            if (typeIdx >= 0)
                selects.Add($"\"{cols.Order[typeIdx]}\"");
            cmd.CommandText = $"SELECT {string.Join(", ", selects)} FROM \"{table}\"";

            using var r = cmd.ExecuteReader();
            var nameOrdinal = nameIdx >= 0 ? r.GetOrdinal(cols.Order[nameIdx]) : -1;
            var typeOrdinal = typeIdx >= 0 ? r.GetOrdinal(cols.Order[typeIdx]) : -1;
            while (r.Read())
            {
                var code = r.GetInt64(0);
                var name = nameOrdinal >= 0 && !r.IsDBNull(nameOrdinal) ? r.GetString(nameOrdinal) : string.Empty;
                var type = typeOrdinal >= 0 && !r.IsDBNull(typeOrdinal) ? r.GetString(typeOrdinal) : string.Empty;
                result[code] = (name, type);
            }
            return result;
        }
        catch (SqliteException)
        {
            return new Dictionary<long, (string, string)>();
        }
    }

    private static EventLogEntry MapRow(
        SqliteDataReader reader,
        Dictionary<string, int> roleIndex,
        string sourceFile,
        IReadOnlyDictionary<long, string> userNames,
        IReadOnlyDictionary<long, string> computerNames,
        IReadOnlyDictionary<long, string> appNames,
        IReadOnlyDictionary<long, string> eventNames,
        IReadOnlyDictionary<long, (string Name, string Type)> metadata)
    {
        var entry = new EventLogEntry
        {
            RowId = reader.GetInt64(0),
            SourceFile = sourceFile,
        };

        var dateValue = ReadLong(reader, roleIndex, "date");
        entry.Timestamp = dateValue is null ? DateTime.MinValue : FromPlatformDate(dateValue.Value);

        var tranValue = ReadLong(reader, roleIndex, "tran");
        entry.TransactionStatus = tranValue is null
            ? EventLogTransactionStatus.None
            : MapTransactionStatus(tranValue.Value);

        var userCode = ReadLong(reader, roleIndex, "user");
        entry.User = ResolveName(userCode, userNames);

        var computerCode = ReadLong(reader, roleIndex, "computer");
        entry.Computer = ResolveName(computerCode, computerNames);

        var appCode = ReadLong(reader, roleIndex, "app");
        entry.Application = ResolveName(appCode, appNames);

        var eventCode = ReadLong(reader, roleIndex, "event");
        entry.EventCode = (int)(eventCode ?? 0);
        entry.EventName = ResolveName(eventCode, eventNames);

        var sevValue = ReadLong(reader, roleIndex, "sev");
        entry.Severity = sevValue is null ? EventLogSeverity.Info : MapSeverity(sevValue.Value);

        entry.Comment = ReadString(reader, roleIndex, "comment");
        entry.Data = ReadString(reader, roleIndex, "data");
        entry.DataPresentation = ReadString(reader, roleIndex, "dataPresentation");
        entry.Session = ReadLong(reader, roleIndex, "session") ?? 0;

        var metaCode = ReadLong(reader, roleIndex, "metadata");
        if (metaCode is not null && metadata.TryGetValue(metaCode.Value, out var meta))
        {
            entry.MetadataName = meta.Name;
            entry.MetadataType = meta.Type;
        }

        return entry;
    }

    private static string ResolveName(long? code, IReadOnlyDictionary<long, string> names) =>
        code is null ? string.Empty : names.TryGetValue(code.Value, out var name) ? name : $"#{code.Value}";

    private static long? ReadLong(SqliteDataReader reader, Dictionary<string, int> roleIndex, string role)
    {
        if (!roleIndex.TryGetValue(role, out var index) || reader.IsDBNull(index))
            return null;
        return reader.GetInt64(index);
    }

    private static string ReadString(SqliteDataReader reader, Dictionary<string, int> roleIndex, string role)
    {
        if (!roleIndex.TryGetValue(role, out var index) || reader.IsDBNull(index))
            return string.Empty;
        return reader.GetString(index);
    }

    /// <summary>
    /// Важность SQLite: 0 — информация, 1 — предупреждение, 2 — ошибка,
    /// 3 — примечание (порядок конфигуратора 1С). Неизвестные коды — информация.
    /// </summary>
    private static EventLogSeverity MapSeverity(long code) => code switch
    {
        1 => EventLogSeverity.Warning,
        2 => EventLogSeverity.Error,
        3 => EventLogSeverity.Note,
        _ => EventLogSeverity.Info,
    };

    /// <summary>Статус транзакции SQLite: 0 — нет, 1 — начата, 2 — зафиксирована, 3 — отменена.</summary>
    private static EventLogTransactionStatus MapTransactionStatus(long value) => value switch
    {
        1 => EventLogTransactionStatus.Started,
        2 => EventLogTransactionStatus.Committed,
        3 => EventLogTransactionStatus.RolledBack,
        _ => EventLogTransactionStatus.None,
    };
}