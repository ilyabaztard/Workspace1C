using System;
using System.Collections.Generic;
using System.Linq;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Результат миграции баз между кластерами 1С (issue #41): список перенесённых баз,
/// базы для добавления в список приложения (в новой группе — имя кластера приёмника),
/// новый сервер/порт для строки подключения и флаг необходимости замены строки
/// подключения в списке приложения. Создаётся VM окна миграции
/// (<see cref="ViewModels.ClusterMigrationViewModel"/>) при успешном завершении и
/// передаётся в MainViewModel.
/// </summary>
/// <param name="MigratedBaseNames">Имена успешно перенесённых баз (порядок переноса).</param>
/// <param name="AddedInfobases">Базы для добавления в список приложения (группа — имя кластера приёмника).</param>
/// <param name="NewServer">Хост нового кластера для строки подключения («cluster info» или адрес сервера назначения).</param>
/// <param name="NewPort">Порт нового кластера.</param>
/// <param name="ReplaceConnectionStrings">
/// true — пользователь подтвердил замену строки подключения («Хотите заменить строку подключения?»);
/// MainViewModel заменит сервер/порт в базах списка приложения.
/// </param>
public sealed record ClusterMigrationResult(
    IReadOnlyList<string> MigratedBaseNames,
    IReadOnlyList<Infobase> AddedInfobases,
    string NewServer,
    int NewPort,
    bool ReplaceConnectionStrings);

/// <summary>
/// Чистая логика миграции информационных баз между кластерами серверов 1С (issue #41):
/// построение запроса создания базы в новом кластере «с теми же данными, что в старом»,
/// фильтрация списка серверов назначения (исключение сервера-источника) и замена
/// сервера/порта в строках подключения баз списка приложения. Не зависит от платформы
/// и UI — покрыта тестами (как <see cref="RacInfobaseMapper"/>).
/// </summary>
public static class ClusterMigrationService
{
    /// <summary>
    /// Строит запрос создания информационной базы в новом кластере (rac «infobase create»)
    /// «с теми же данными, что в старом кластере»: имя, описание, СУБД, сервер СУБД,
    /// имя БД в СУБД, локаль и уровень безопасности копируются из записи «infobase summary
    /// list» источника. Пользователь СУБД тоже берётся из источника — rac отдаёт db-user
    /// в «infobase summary list» (issue #41, фикс 1.0.0.72): <paramref name="dbUser"/> (диалог)
    /// используется как fallback для баз без db-user у rac; пароль СУБД rac НЕ возвращает
    /// (см. <see cref="RacInfobaseInfo"/>: «rac не возвращает пароль») — всегда из диалога
    /// <paramref name="dbPassword"/>. База данных на сервере СУБД уже существует (старый
    /// кластер подключался к ней) — <see cref="RacInfobaseCreateRequest.CreateDb"/> = false,
    /// переносим ТОЛЬКО регистрацию базы в новом кластере.
    /// Имя БД в СУБД переносится АВТОМАТИЧЕСКИ (issue #41, фикс 1.0.0.71; поле «Имя БД» на
    /// форме миграции удалено): приоритет у <see cref="RacInfobaseSummary.DbName"/> источника,
    /// а для баз, где rac не вернул db-name в «infobase summary list» (см.
    /// <see cref="RacOutputParser.ToInfobaseSummaries"/> — отдельные версии rac не выводят
    /// колонку/ключ db-name; комментарий 7OH «выяснить, почему rac не возвращает db-name»),
    /// используется имя информационной базы <see cref="RacInfobaseSummary.Name"/>: при создании
    /// ИБ сервер 1С по умолчанию создаёт БД в СУБД с именем, равным имени ИБ, поэтому это
    /// корректный fallback без лишних запросов к rac.
    /// </summary>
    /// <param name="source">Запись базы источника («infobase summary list» старого кластера).</param>
    /// <param name="dbUser">Пользователь СУБД из диалога миграции; fallback при пустом db-user у rac.</param>
    /// <param name="dbPassword">Пароль СУБД (диалог; маскируется в журнале RacClient).</param>
    /// <param name="overrideDbms">
    /// Тип СУБД с формы миграции (issue #41): при непустом значении приоритетнее, чем
    /// <see cref="RacInfobaseSummary.Dbms"/> источника (приёмник может быть на другом сервере
    /// СУБД, rac требует непустые dbms/db-server). Пусто — значения источника.
    /// </param>
    /// <param name="overrideDbServer">
    /// Сервер СУБД с формы миграции (issue #41); приоритет над
    /// <see cref="RacInfobaseSummary.DbServer"/> источника. Пусто — значения источника.
    /// </param>
    /// <param name="overrideLocale">
    /// Локаль с формы миграции (issue #41, комментарий 7OH «Поле с локалью на поле не
    /// замечено»): приоритет над <see cref="RacInfobaseSummary.Locale"/> источника. rac
    /// принимает двухбуквенные коды — значение нормализуется через <see cref="RacLocales.Normalize"/>.
    /// </param>
    public static RacInfobaseCreateRequest BuildCreateRequest(
        RacInfobaseSummary source,
        string dbUser,
        string dbPassword,
        string overrideDbms = "",
        string overrideDbServer = "",
        string overrideLocale = "")
    {
        ArgumentNullException.ThrowIfNull(source);

        return new RacInfobaseCreateRequest
        {
            Name = source.Name ?? string.Empty,
            Description = source.Descr ?? string.Empty,
            Dbms = FirstNonEmpty(overrideDbms, source.Dbms),
            DbServer = FirstNonEmpty(overrideDbServer, source.DbServer),
            // Перенос имени БД автоматически: значение rac, а для баз без db-name в summary
            // list — имя ИБ (сервер 1С создаёт БД в СУБД по умолчанию с именем ИБ).
            DbName = FirstNonEmpty(source.DbName, source.Name ?? string.Empty),
            // Пользователь СУБД берётся из записи источника (rac отдаёт db-user в
            // «infobase summary list»); значение формы — только fallback для баз без db-user
            // у rac. Пароль rac не возвращает — всегда из диалога (issue #41, фикс 1.0.0.72).
            DbUser = FirstNonEmpty(source.DbUser, dbUser),
            DbPassword = dbPassword ?? string.Empty,
            // Локаль: форма (автозаполнена из источника) — приоритет; rac отдаёт ее в
            // «infobase summary list» не всегда. Двухбуквенный код — как принимает rac.
            Locale = Models.RacLocales.Normalize(FirstNonEmpty(overrideLocale, source.Locale ?? string.Empty)),
            SecurityLevel = source.SecurityLevel,
            // БД на сервере СУБД уже существует — только регистрируем базу в новом кластере.
            CreateDb = false,
            ScheduledJobsDeny = false
        };
    }

    /// <summary>Первое непустое значение (<paramref name="values"/>; переопределение пользователя
    /// приоритетнее значения источника, пустое/пробельное пропускается). Пусто — пустая строка.</summary>
    private static string FirstNonEmpty(params string[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }
        return string.Empty;
    }

    /// <summary>
    /// Список серверов назначения для диалога миграции (issue #41): записи справочника
    /// «Серверы 1С» БЕЗ сервера-источника (выбранного в форме импорта). Исключение — по
    /// <see cref="ManagementServer.Id"/> (регистронезависимо); у источника без Id (редкий
    /// случай) исключение не выполняется. Null-элементы отбрасываются. Возвращает новый
    /// список — исходный не мутируется.
    /// </summary>
    public static IReadOnlyList<ManagementServer> ExcludeSourceServer(
        IReadOnlyList<ManagementServer>? servers,
        ManagementServer? sourceServer)
    {
        var result = new List<ManagementServer>();
        if (servers is null)
            return result;

        var sourceId = (sourceServer?.Id ?? string.Empty).Trim();
        var hasSourceId = sourceId.Length > 0;

        foreach (var server in servers)
        {
            if (server is null)
                continue;
            if (hasSourceId && string.Equals(server.Id?.Trim(), sourceId, StringComparison.OrdinalIgnoreCase))
                continue;
            result.Add(server);
        }

        return result;
    }

    /// <summary>
    /// Заменяет сервер и порт в строках подключения клиент-серверных баз списка приложения,
    /// чьё имя базы (<see cref="ConnectionSettings.DatabaseName"/>, Ref) совпадает с одним из
    /// имён перенесённых баз (<paramref name="baseNames"/>, сравнение без учёта регистра).
    /// Файловые/веб-базы и базы без совпадения не трогаются. Возвращает число заменённых
    /// баз. Используется MainViewModel после миграции («Хотите заменить строку подключения?»)
    /// — базы с новым кластером сразу указывают на новый сервер/порт.
    /// </summary>
    /// <param name="infobases">Список приложения (реальный, не снимок — мутируется).</param>
    /// <param name="baseNames">Имена перенесённых баз (Ref в строке подключения).</param>
    /// <param name="newServer">Хост нового кластера.</param>
    /// <param name="newPort">Порт нового кластера.</param>
    public static int ReplaceServerAndPort(
        IReadOnlyList<Infobase>? infobases,
        IReadOnlyCollection<string>? baseNames,
        string newServer,
        int newPort)
    {
        if (infobases is null || baseNames is null || baseNames.Count == 0)
            return 0;

        var names = new HashSet<string>(
            baseNames.Where(n => !string.IsNullOrWhiteSpace(n)),
            StringComparer.OrdinalIgnoreCase);
        if (names.Count == 0)
            return 0;

        var server = (newServer ?? string.Empty).Trim();
        if (server.Length == 0)
            return 0;

        var replaced = 0;
        foreach (var infobase in infobases)
        {
            var connection = infobase?.Connection;
            if (connection is null || connection.Type != ConnectionType.ClientServer)
                continue;
            if (string.IsNullOrWhiteSpace(connection.DatabaseName))
                continue;
            if (!names.Contains(connection.DatabaseName))
                continue;

            connection.Server = server;
            if (newPort > 0)
                connection.Port = newPort;
            replaced++;
        }

        return replaced;
    }

    /// <summary>Порт кластера сервера 1С по умолчанию (1541; порт агента ragent — 1540, см. <see cref="IRacClient.DefaultPort"/>).</summary>
    public const int DefaultClusterPort = 1541;

    /// <summary>
    /// Имя создаваемой при миграции группы (issue #41, комментарий 7OH: «Создало папку
    /// "Локальный кластер"… Надо или имя сервера из списка добавлять или порт, если не
    /// стандартный. А можно и то и другое. Пример: "Платформа 27 - Локальный кластер
    /// (27541)"»): имя кластера приёмника с префиксом хоста сервера и суффиксом порта,
    /// когда порт НЕ стандартный (не 1540 — порт ragent/RAS — и не 1541 — порт кластера
    /// по умолчанию). Пустой хост — только имя кластера (прежнее поведение, обратная
    /// совместимость). Имя детерминировано: повторная миграция в тот же кластер-приёмник
    /// даёт ту же строку, и дубликаты групп при добавлении не создаются (сверка по имени в
    /// <c>MainViewModel.ApplyClusterMigrationResult</c> — как в импорте).
    /// </summary>
    public static string BuildGroupName(
        string? serverHost, string? clusterName, int newPort, int defaultPort = IRacClient.DefaultPort)
    {
        var cluster = (clusterName ?? string.Empty).Trim();
        if (cluster.Length == 0)
            return string.Empty;

        var host = (serverHost ?? string.Empty).Trim();
        var name = host.Length > 0 ? host + " - " + cluster : cluster;
        // Суффикс порта — только для НЕстандартного порта: 1540 и 1541 — значения по
        // умолчанию 1С (порт ragent и порт кластера), они не должны попадать в имя —
        // иначе стандартный кластер выглядел бы в группе как нестандартный.
        var isStandardPort = newPort == defaultPort || newPort == DefaultClusterPort;
        if (newPort > 0 && !isStandardPort)
            name += " (" + newPort + ")";
        return name;
    }

    /// <summary>
    /// Хост строки подключения после миграции (issue #41): приоритет у имени кластера из
    /// «cluster info» (<paramref name="clusterHostName"/> — корректен для удалённого RAS,
    /// как в <see cref="RacInfobaseMapper"/>); иначе — host-часть адреса сервера назначения
    /// <paramref name="serverAddress"/> с удалённым портом (порт RAS в строку подключения
    /// клиентов не попадает — там порт КЛАСТЕРА, отдельный параметр).
    /// </summary>
    public static string ResolveClusterHost(string serverAddress, string? clusterHostName)
    {
        var host = (clusterHostName ?? string.Empty).Trim();
        if (host.Length > 0)
            return host;

        // IPv6 в квадратных скобках ([2001:db8::1]:1545) сохраняется с ними — так
        // строку подключения 1С читает ParseServerAndPort и GetServerWithPort.
        var parsed = new ConnectionSettings();
        ConnectionSettings.ParseServerAndPort(serverAddress, parsed);
        return parsed.Server.Trim();
    }
}
