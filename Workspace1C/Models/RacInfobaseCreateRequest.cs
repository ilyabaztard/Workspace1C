namespace Workspace1C.Models;

/// <summary>
/// Параметры создания информационной базы в кластере серверов 1С (issue #3, вкладка
/// «Базы» окна «Серверы 1С»): поля диалога → команда rac «infobase create». В кластере
/// бывают только клиент-серверные базы (комментарий 7OH от 2026-10-18 20:24), поэтому
/// <see cref="Dbms"/> — одно из значений rac «--dbms»: MSSQLServer, PostgreSQL,
/// IBMDB2, OracleDatabase; <see cref="DbServer"/>/<see cref="DbName"/> — сервер и имя
/// БД на сервере СУБД. Только примитивные значения — диалог (WPF/Avalonia) не зависит
/// от ViewModel.
/// </summary>
public sealed class RacInfobaseCreateRequest
{
    /// <summary>Имя информационной базы в кластере (параметр rac «--name»).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Описание информационной базы (параметр rac «--desc», необязательный).</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Тип СУБД (параметр rac «--dbms»): MSSQLServer, PostgreSQL, IBMDB2, OracleDatabase.
    /// </summary>
    public string Dbms { get; set; } = "MSSQLServer";

    /// <summary>Сервер СУБД (параметр rac «--db-server»).</summary>
    public string DbServer { get; set; } = string.Empty;

    /// <summary>Имя базы данных на сервере СУБД (параметр rac «--db-name»).</summary>
    public string DbName { get; set; } = string.Empty;

    /// <summary>Пользователь СУБД (параметр rac «--db-user»; для файловой базы не нужен).</summary>
    public string DbUser { get; set; } = string.Empty;

    /// <summary>Пароль пользователя СУБД (параметр rac «--db-pwd»; маскируется в журнале).</summary>
    public string DbPassword { get; set; } = string.Empty;

    /// <summary>Локаль информационной базы (параметр rac «--locale»; пусто — по умолчанию сервера).</summary>
    public string Locale { get; set; } = string.Empty;

    /// <summary>Уровень безопасности (параметр rac «--security-level», 0/1/2); null — по умолчанию.</summary>
    public int? SecurityLevel { get; set; }

    /// <summary>Создавать базу данных на сервере СУБД (параметр rac «--create-db»).</summary>
    public bool CreateDb { get; set; }

    /// <summary>Запретить выполнение регламентных заданий (параметр rac «--scheduled-jobs-deny»).</summary>
    public bool ScheduledJobsDeny { get; set; }

    /// <summary>Блокировка начала сеансов (параметр rac «--sessions-deny=on»; null — не
    /// передаётся, база создаётся без блокировки). Переносится из базы-источника при
    /// миграции/копировании, только когда флаг фактически выставлен (issue #69).</summary>
    public bool? SessionsDeny { get; set; }
}