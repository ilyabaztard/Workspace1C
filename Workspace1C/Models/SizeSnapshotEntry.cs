namespace Workspace1C.Models;

/// <summary>
/// Дневной снимок размеров информационной базы (п.7 «История размеров баз и прогноз
/// диска», выпуск 1.0.0.89): размер файловой ИБ, объём каталога копий базы и свободное
/// место на диске на конкретную дату. Ключ записи — <see cref="DateUtc"/> (день) +
/// <see cref="InfobaseId"/>. Хранится в <c>size_history.json</c> (см. Services.SizeHistoryStore)
/// и дополняет текущий кеш <see cref="FileSizeCacheEntry"/>, который хранит только
/// последний замер.
/// </summary>
public sealed class SizeSnapshotEntry
{
    /// <summary>День снимка (UTC, нормализован к началу суток по UTC).</summary>
    public DateTime DateUtc { get; set; }

    /// <summary>Идентификатор базы (<see cref="Infobase.Id"/>); пусто — снимок «всех баз».</summary>
    public string InfobaseId { get; set; } = string.Empty;

    /// <summary>Имя базы на момент снимка (для UI без обращения к списку баз).</summary>
    public string InfobaseName { get; set; } = string.Empty;

    /// <summary>Размер файловой ИБ в байтах (каталог целиком или файл 1Cv8.1CD); null — неизвестно.</summary>
    public long? SizeBytes { get; set; }

    /// <summary>
    /// Объём каталога копий базы в байтах (основной каталог назначения сценария
    /// резервирования либо <c>AppSettings.BackupTargetDirectories</c>); null — каталог копий
    /// не определён или недоступен.
    /// </summary>
    public long? CopiesBytes { get; set; }

    /// <summary>Имя диска каталога копий (<c>Path.GetPathRoot</c>), для прогноза «диск под копиями».</summary>
    public string? CopiesDrive { get; set; }

    /// <summary>Свободное место на диске каталога копий в байтах (момент снимка); null — недоступно.</summary>
    public long? CopiesDiskFreeBytes { get; set; }

    /// <summary>Помечено вручную (кнопка «Замерить сейчас»); автоматические снимки — false.</summary>
    public bool Manual { get; set; }
}
