using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Models;
using Workspace1C.Services.EventLog;

namespace Workspace1C.Services.Mcp;

/// <summary>
/// Реализации обработчиков инструментов MCP-сервера (п.6 «MCP-сервер и расширение CLI»,
/// выпуск 1.0.0.88). Переиспользуют существующие сервисы Workspace 1C (репозиторий баз,
/// запуск, клонирование, журнал регистрации, rac, задания по расписанию) в headless-
/// режиме; сервисы берутся из <see cref="AppServices"/> — контейнер инициализируется
/// в <see cref="CliEntryPoint.TryHandle"/> до старта MCP-цикла.
/// </summary>
internal static class McpToolHandlers
{
    /// <summary>Таймаут ожидания диалога подтверждения Telegram по умолчанию (секунд).</summary>
    private const int DefaultConfirmTimeoutSeconds = 300;

    // ------------------------------------------------------------- list_infobases

    public static async Task<object> HandleListInfobases(JsonObject? parameters, CancellationToken ct)
    {
        await Task.CompletedTask;
        var repository = AppServices.GetRequiredService<IInfobaseRepository>();
        var profileService = AppServices.GetRequiredService<IProfileService>();
        var includePrivate = McpJsonRpc.GetBool(parameters, "includePrivate", false);

        var visible = repository.Load()
            .Where(b => includePrivate || !b.IsPrivate || profileService.CanShowPrivateBases)
            .OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new
        {
            bases = visible.Select(ib => new
            {
                name = ib.Name,
                kind = KindOf(ib),
                id = ib.Id,
                group = ib.Group,
                connection = ib.Connection?.ToConnectionString() ?? string.Empty,
                tags = ib.Tags ?? new List<string>(),
                isPrivate = ib.IsPrivate,
                isFavorite = ib.IsFavorite,
                isPinned = ib.IsPinned,
                lastLaunch = ib.LastLaunchDate,
                lastBackup = ib.LastBackupUtc
            }).ToList(),
            total = visible.Count
        };
    }

    // ----------------------------------------------------------- launch_infobase

    public static async Task<object> HandleLaunchInfobase(JsonObject? parameters, CancellationToken ct)
    {
        await Task.CompletedTask;
        var name = McpJsonRpc.GetString(parameters, "name");
        if (string.IsNullOrWhiteSpace(name))
            throw new McpToolException("Параметр name (имя или id базы) обязателен.");

        var infobase = FindInfobase(name)!;
        var profileService = AppServices.GetRequiredService<IProfileService>();
        if (infobase.IsPrivate && !profileService.CanShowPrivateBases)
            throw new McpToolException($"База «{infobase.Name}» приватная: разблокируйте приватные базы в приложении.");

        var designer = McpJsonRpc.GetBool(parameters, "designer", false);
        var mode = designer ? OneCLaunchMode.Configurator : OneCLaunchMode.Enterprise;
        var launcher = AppServices.GetRequiredService<IOneCLauncher>();
        var ok = launcher.Launch(infobase, mode);

        if (!ok)
            throw new McpToolException($"Не удалось запустить «{infobase.Name}» ({(designer ? "configurator" : "enterprise")}).");

        return new
        {
            launched = true,
            name = infobase.Name,
            mode = designer ? "configurator" : "enterprise"
        };
    }

    // ------------------------------------------------------------ clone_infobase

    public static async Task<object> HandleCloneInfobase(JsonObject? parameters, CancellationToken ct)
    {
        var sourceName = McpJsonRpc.GetString(parameters, "source");
        if (string.IsNullOrWhiteSpace(sourceName))
            throw new McpToolException("Параметр source (имя или id исходной базы) обязателен.");

        var server = McpJsonRpc.GetString(parameters, "server");
        var databaseName = McpJsonRpc.GetString(parameters, "databaseName");
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(databaseName))
            throw new McpToolException("Параметры server и databaseName (целевая база на сервере 1С) обязательны.");

        var source = FindInfobase(sourceName)!;
        if (source.Connection?.Type != ConnectionType.ClientServer)
            throw new McpToolException(
                $"Копия поддерживается только для клиент-серверных баз; «{source.Name}» — {KindOf(source)}.");

        var modeValue = (McpJsonRpc.GetString(parameters, "mode") ?? "full").Trim().ToLowerInvariant();
        var mode = modeValue switch
        {
            "config" => ServerCloneMode.ConfigurationOnly,
            _ => ServerCloneMode.FullCopy
        };

        var request = new ServerCloneRequest
        {
            Source = source,
            CloneName = McpJsonRpc.GetString(parameters, "cloneName") ?? source.Name + " — копия",
            Mode = mode,
            PlatformVersion = McpJsonRpc.GetString(parameters, "platformVersion") ?? source.PlatformVersion,
            Server = server,
            DatabaseName = databaseName,
            Dbms = McpJsonRpc.GetString(parameters, "dbms") ?? string.Empty,
            DbServer = McpJsonRpc.GetString(parameters, "dbServer") ?? string.Empty,
            DbName = McpJsonRpc.GetString(parameters, "dbName") ?? databaseName,
            DbUser = McpJsonRpc.GetString(parameters, "dbUser") ?? string.Empty,
            DbPassword = McpJsonRpc.GetString(parameters, "dbPassword") ?? string.Empty,
            CreateSqlDatabase = McpJsonRpc.GetBool(parameters, "createSqlDatabase", false),
            BlockScheduledJobs = McpJsonRpc.GetBool(parameters, "blockScheduledJobs", false)
        };

        var validation = ServerClonePlanner.Validate(request);
        if (validation != ServerCloneValidationError.None)
            throw new McpToolException(
                validation == ServerCloneValidationError.NoPlatform
                    ? "Не найдена платформа 1С для копирования: укажите platformVersion или установите платформу."
                    : "Невалидные параметры копирования базы (проверьте server/databaseName/dbms).");

        // Клонирование выполняется в фоне (ServerCloneService); команды DESIGNER могут
        // длиться десятки минут — ждём завершения и возвращаем подключение копии.
        var connection = await new ServerCloneService().CloneAsync(request, progress: null, ct).ConfigureAwait(false);

        return new
        {
            cloned = true,
            mode = mode == ServerCloneMode.ConfigurationOnly ? "config" : "full",
            name = request.CloneName,
            connection = connection.ToConnectionString()
        };
    }

    // ------------------------------------------------------------ event_log_query

    public static async Task<object> HandleEventLogQuery(JsonObject? parameters, CancellationToken ct)
    {
        await Task.CompletedTask;
        var name = McpJsonRpc.GetString(parameters, "name");
        var path = McpJsonRpc.GetString(parameters, "path");

        // Источник журнала: явный каталог файловой базы (--path) либо база по имени/id.
        JournalLocation location;
        if (!string.IsNullOrWhiteSpace(path))
        {
            location = LgdJournalLocator.FromDirectory(
                System.IO.Path.Combine(path.Trim(), LgdJournalLocator.LogDirectoryName));
        }
        else
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new McpToolException("Укажите name (база) или path (каталог файловой ИБ).");
            var infobase = FindInfobase(name)!;
            location = LgdJournalLocator.Resolve(infobase);
        }

        if (!location.IsValid)
            throw new McpToolException(location.ErrorMessage ?? "Журнал регистрации не найден.");

        var query = new EventLogQuery
        {
            From = ParseDateTime(McpJsonRpc.GetString(parameters, "from"), DateTime.Today) ?? DateTime.Today,
            To = ParseDateTime(McpJsonRpc.GetString(parameters, "to"), null) ?? DateTime.MaxValue,
            User = McpJsonRpc.GetString(parameters, "user") ?? string.Empty,
            Event = McpJsonRpc.GetString(parameters, "event") ?? string.Empty,
            Text = McpJsonRpc.GetString(parameters, "text") ?? string.Empty,
            MaxRows = McpJsonRpc.GetInt(parameters, "limit") ?? EventLogQuery.DefaultMaxRows,
            IncludeInfo = true,
            IncludeWarning = true,
            IncludeError = true,
            IncludeNote = true
        };

        var result = EventLogQueryService.Query(location, query, ct);
        if (result.Error is not null)
            throw new McpToolException($"Ошибка чтения журнала: {result.Error}");

        return new
        {
            location = location.LogDir,
            format = location.FormatDisplay,
            truncated = result.Truncated,
            count = result.Entries.Count,
            entries = result.Entries.Select(e => new
            {
                timestamp = e.Timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                severity = SeverityName(e.Severity),
                user = e.User,
                computer = e.Computer,
                application = e.Application,
                @event = string.IsNullOrEmpty(e.EventName)
                    ? e.EventCode.ToString(CultureInfo.InvariantCulture)
                    : e.EventName,
                metadata = string.IsNullOrEmpty(e.MetadataName) ? null : e.MetadataName,
                comment = e.Comment,
                data = e.Data,
                dataPresentation = e.DataPresentation,
                session = e.Session == 0 ? (long?)null : e.Session
            }).ToList()
        };
    }

    // ------------------------------------------------------------ server_sessions

    public static async Task<object> HandleServerSessions(JsonObject? parameters, CancellationToken ct)
    {
        var serverKey = McpJsonRpc.GetString(parameters, "server");
        if (string.IsNullOrWhiteSpace(serverKey))
            throw new McpToolException("Параметр server (id/описание/адрес записи справочника «Серверы 1С») обязателен.");
        var clusterId = ParseGuid(McpJsonRpc.GetString(parameters, "clusterId"));
        var limit = McpJsonRpc.GetInt(parameters, "limit");

        var (server, connection) = ResolveManagementServer(serverKey);
        var rac = AppServices.GetRequiredService<IRacClient>();

        var clusters = await rac.GetClustersAsync(connection, ct).ConfigureAwait(false);
        var cluster = clusters.FirstOrDefault(c => clusterId is null || c.Id == clusterId)
                      ?? clusters.FirstOrDefault();
        if (cluster is null)
            throw new McpToolException(
                $"На сервере «{server.Description}» не найдены кластеры 1С{(clusterId is null ? string.Empty : " (по clusterId)")}.");

        var sessions = await rac.GetSessionsAsync(connection, cluster.Id, cancellationToken: ct).ConfigureAwait(false);

        var items = sessions;
        if (limit is > 0)
            items = items.Take(limit.Value).ToList();

        return new
        {
            server = server.Description,
            clusterId = cluster.Id,
            total = sessions.Count,
            count = items.Count,
            sessions = items.Select(s => new
            {
                id = s.Id,
                infobaseId = s.InfobaseId,
                user = s.User,
                host = s.Host,
                appId = s.AppId,
                startedAt = s.StartedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                lastActiveAt = s.LastActiveAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                state = s.State,
                blockedByLs = s.BlockedByLs,
                blockedByDeadlock = s.BlockedByDeadlock,
                memoryBytes = s.Memory,
                durationMs = s.DurationAll,
                bytesRead = s.Read,
                bytesWritten = s.Write,
                sessionConnectionId = s.ConnectionId,
                hibernate = s.Hibernate
            }).ToList()
        };
    }

    // ---------------------------------------------------------- scheduled_jobs_state

    public static async Task<object> HandleScheduledJobs(JsonObject? parameters, CancellationToken ct)
    {
        await Task.CompletedTask;
        var store = AppServices.GetRequiredService<IScheduledTaskStore>();
        var jobs = store.LoadAll();

        return new
        {
            count = jobs.Count,
            jobs = jobs.Select(j => new
            {
                id = j.Id,
                name = j.Name,
                kind = j.Kind.ToString(),
                enabled = j.Enabled,
                time = j.Time,
                daysOfWeek = j.DaysOfWeek.Select(d => d.ToString()).ToList(),
                repeat = j.RepeatEnabled
                    ? $"{j.RepeatIntervalValue} {j.RepeatIntervalUnit.ToString().ToLowerInvariant()}"
                    : null,
                infobaseId = j.InfobaseId,
                lastRunAt = j.LastRunAt,
                lastRunSuccess = j.LastRunSuccess,
                lastRunMessage = j.LastRunMessage
            }).ToList()
        };
    }

    // ----------------------------------------------------------- terminate_session

    public static async Task<object> HandleTerminateSession(
        McpSafetyGate safetyGate, JsonObject? parameters, CancellationToken ct)
    {
        var serverKey = McpJsonRpc.GetString(parameters, "server");
        var sessionId = ParseGuid(McpJsonRpc.GetString(parameters, "sessionId"));
        if (string.IsNullOrWhiteSpace(serverKey) || sessionId is null)
            throw new McpToolException("Параметры server и sessionId (идентификатор завершаемого сеанса) обязательны.");

        // ---- Ворота безопасности: без подтверждения Telegram операция НЕ выполняется. ----
        // Подтверждение запрашивается ДО обращения к справочнику серверов и rac:
        // при отказе/таймауте никакие внешние сервисы не трогаются.
        var timeoutSeconds = McpJsonRpc.GetInt(parameters, "confirmTimeoutSeconds") ?? DefaultConfirmTimeoutSeconds;
        var prompt = $"Завершить сеанс {sessionId} на сервере «{serverKey}»? Это отключит активного пользователя.";
        var confirmation = await safetyGate.RequireApprovalAsync(
            prompt, TimeSpan.FromSeconds(timeoutSeconds), ct).ConfigureAwait(false);

        if (!confirmation.Approved)
        {
            var reason = confirmation.Outcome switch
            {
                McpConfirmOutcome.Timeout => "timeout",
                McpConfirmOutcome.NotConfigured => "not_configured",
                _ => "denied"
            };
            var detail = string.IsNullOrWhiteSpace(confirmation.Detail)
                ? "Операция не подтверждена через Telegram."
                : confirmation.Detail;
            throw new McpToolException($"Действие не выполнено: {reason}. {detail}");
        }

        var (server, connection) = ResolveManagementServer(serverKey);
        var rac = AppServices.GetRequiredService<IRacClient>();

        var clusterId = ParseGuid(McpJsonRpc.GetString(parameters, "clusterId"));
        var clusters = await rac.GetClustersAsync(connection, ct).ConfigureAwait(false);
        var cluster = clusters.FirstOrDefault(c => clusterId is null || c.Id == clusterId)
                      ?? clusters.FirstOrDefault();
        if (cluster is null)
            throw new McpToolException($"На сервере «{server.Description}» не найдены кластеры 1С.");

        var processId = ParseGuid(McpJsonRpc.GetString(parameters, "processId"));
        var terminated = await rac.TerminateSessionAsync(
            connection, cluster.Id, sessionId.Value, processId, ct).ConfigureAwait(false);

        if (!terminated)
        {
            var error = string.IsNullOrWhiteSpace(rac.LastActionError) ? "rac: команда завершилась с ошибкой" : rac.LastActionError;
            throw new McpToolException($"Не удалось завершить сеанс: {error}");
        }

        return new
        {
            terminated = true,
            sessionId = sessionId!.Value,
            clusterId = cluster.Id,
            server = server.Description,
            confirmation = new { result = confirmation.MachineName }
        };
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Тип подключения базы: server/web/file (совпадает с CLI-контрактом).</summary>
    private static string KindOf(Infobase ib) => ib.Connection?.Type switch
    {
        ConnectionType.ClientServer => "server",
        ConnectionType.WebServer => "web",
        _ => "file"
    };

    /// <summary>Поиск базы по id, затем по имени без учёта регистра.</summary>
    private static Infobase? FindInfobase(string target)
    {
        var repository = AppServices.GetRequiredService<IInfobaseRepository>();
        var bases = repository.Load();
        return bases.FirstOrDefault(b => string.Equals(b.Id, target, StringComparison.OrdinalIgnoreCase))
               ?? bases.FirstOrDefault(b =>
                   string.Equals(b.Name?.Trim(), target.Trim(), StringComparison.OrdinalIgnoreCase))
               ?? throw new McpToolException($"База не найдена: '{target}'");
    }

    /// <summary>Находит запись справочника «Серверы 1С» и строит параметры подключения rac.</summary>
    private static (ManagementServer Server, RacConnectionParams Connection) ResolveManagementServer(string key)
    {
        var store = AppServices.GetRequiredService<IServerListStore>();
        var serverKey = key.Trim();
        var server = store.Load().FirstOrDefault(s =>
            string.Equals(s.Id, serverKey, StringComparison.OrdinalIgnoreCase)
            || string.Equals(s.Description, serverKey, StringComparison.OrdinalIgnoreCase)
            || string.Equals(s.Address, serverKey, StringComparison.OrdinalIgnoreCase));

        if (server is null)
            throw new McpToolException(
                $"Сервер «{key}» не найден в справочнике «Серверы 1С» (заполните его в приложении: «Серверы 1С» → «Добавить»).");

        var (host, port) = RacConnectionAddress.Split(server.Address, server.Port);
        var connection = new RacConnectionParams
        {
            Address = host,
            Port = port,
            User = server.User,
            Password = server.Password
        };
        return (server, connection);
    }

    private static DateTime? ParseDateTime(string? value, DateTime? fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;
        return DateTime.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : fallback;
    }

    private static Guid? ParseGuid(string? value) =>
        Guid.TryParse(value, out var guid) ? guid : null;

    private static string SeverityName(EventLogSeverity severity) => severity switch
    {
        EventLogSeverity.Error => "error",
        EventLogSeverity.Warning => "warning",
        EventLogSeverity.Note => "note",
        _ => "info"
    };
}