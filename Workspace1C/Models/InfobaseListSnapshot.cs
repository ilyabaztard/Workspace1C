namespace Workspace1C.Models;

/// <summary>
/// Полный снимок состояния списка баз для экспорта/импорта в JSON (0.3.9.122).
/// В отличие от CSV-экспорта (только видимые базы, без импорта) переносит ВСЁ
/// состояние списка:
/// <list type="bullet">
/// <item>базы со всеми полями — строка подключения (файловая/клиент-серверная/веб),
/// имя, группа (полный путь), теги, закладка 1–9, закрепление, приватность,
/// внешняя обработка, скрипты pre/post запуска, раздельные учётные данные
/// (запуск/хранилище), параметры запуска, порядок сортировки;</item>
/// <item>иерархию групп целиком (ParentId, цвета, иконки);</item>
/// <item>избранное и закрепление (поля <see cref="Infobase.IsFavorite"/>,
/// <see cref="Infobase.FavoriteHotkeyNumber"/>, <see cref="Infobase.IsPinned"/>).</item>
/// </list>
/// Приватные базы попадают в снимок только тогда, когда профиль разблокирован —
/// фильтрация выполняется на уровне ViewModel (<c>IsVisibleForPrivateFilter</c>).
/// </summary>
public class InfobaseListSnapshot
{
    /// <summary>Текущая версия формата снимка (2 — полное состояние списка).</summary>
    public const int CurrentVersion = 2;

    /// <summary>
    /// Версия формата файла. Файлы старых версий (1 — просто базы + группы,
    /// <see cref="InfobaseExportData"/>) десериализуются в этот же класс без ошибок:
    /// недостающих полей нет, структура совпадает.
    /// </summary>
    public int Version { get; set; } = CurrentVersion;

    /// <summary>Список информационных баз со всеми полями.</summary>
    public List<Infobase> Infobases { get; set; } = new();

    /// <summary>Список групп информационных баз (с иерархией ParentId).</summary>
    public List<Group> Groups { get; set; } = new();

    /// <summary>
    /// Заметки баз для переноса (п.8 «Заметки к базе», 1.0.0.90). Переносится markdown-текст
    /// карточки и ИМЕНА секретов; значения секретов зашифрованы ключом исходной машины
    /// (DPAPI Windows / ключ профиля Linux) и с другой машины не расшифруются — поэтому
    /// в JSON-снимок не включаются. Пустой список — заметок нет/не было в снимке.
    /// </summary>
    public List<InfobaseNoteTransfer> Notes { get; set; } = new();
}

/// <summary>
/// Заметка базы в JSON-снимке списка (п.8, 1.0.0.90): ключ базы, markdown-карточка и
/// только имена секретов (значения не переносятся — они зашифрованы ключом исходной
/// машины). Чистый DTO для сериализации.
/// </summary>
public sealed class InfobaseNoteTransfer
{
    /// <summary>Ключ базы (см. <see cref="InfobaseNote.InfobaseId"/>).</summary>
    public string InfobaseId { get; set; } = string.Empty;

    /// <summary>Markdown-текст карточки.</summary>
    public string Markdown { get; set; } = string.Empty;

    /// <summary>Имена секретов (без значений).</summary>
    public List<string> SecretNames { get; set; } = new();

    /// <summary>Дата последнего обновления заметки (UTC).</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}