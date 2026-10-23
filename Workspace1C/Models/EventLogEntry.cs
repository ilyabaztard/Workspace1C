using System;

namespace Workspace1C.Models;

/// <summary>
/// Одна запись журнала регистрации информационной базы 1С. Чистая модель без
/// зависимостей от UI; заполняется ридерами журнала (SQLite-формат .lgd и
/// последовательный формат .lgf/.lgp).
/// </summary>
public sealed class EventLogEntry
{
    /// <summary>
    /// Идентификатор записи: rowid в SQLite-журнале / порядковый номер в
    /// последовательном формате. Используется для стабильной сортировки и
    /// постраничной выборки.
    /// </summary>
    public long RowId { get; set; }

    /// <summary>Момент события (локальное время платформы).</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>Статус транзакции.</summary>
    public EventLogTransactionStatus TransactionStatus { get; set; }

    /// <summary>
    /// Имя пользователя (из словаря кодов) либо текст вида «#код», если
    /// словарь недоступен.
    /// </summary>
    public string User { get; set; } = string.Empty;

    /// <summary>Имя компьютера (или код в виде «#код»).</summary>
    public string Computer { get; set; } = string.Empty;

    /// <summary>Приложение (1CV8, WebClient, BackgroundJob и т. п.).</summary>
    public string Application { get; set; } = string.Empty;

    /// <summary>Числовой код события (индекс в словаре событий журнала).</summary>
    public int EventCode { get; set; }

    /// <summary>
    /// Имя события (например «$Data.Update»); если словарь недоступен — «#код».
    /// </summary>
    public string EventName { get; set; } = string.Empty;

    /// <summary>Важность события.</summary>
    public EventLogSeverity Severity { get; set; }

    /// <summary>Комментарий события.</summary>
    public string Comment { get; set; } = string.Empty;

    /// <summary>Тип метаданных (например «Справочник»), если определён.</summary>
    public string MetadataType { get; set; } = string.Empty;

    /// <summary>Имя объекта метаданных (например «Контрагенты»), если определено.</summary>
    public string MetadataName { get; set; } = string.Empty;

    /// <summary>Данные события (сериализованный дамп изменённых данных).</summary>
    public string Data { get; set; } = string.Empty;

    /// <summary>
    /// Представление данных события, записанное платформой (например «Реализация
    /// товаров 000123 от 01.10.2026»), если есть (выпуск 1.0.0.69).
    /// </summary>
    public string DataPresentation { get; set; } = string.Empty;

    /// <summary>Номер сеанса (0 — неизвестен; выпуск 1.0.0.69).</summary>
    public long Session { get; set; }

    /// <summary>
    /// Файл журнала, из которого прочитана запись (для трассировки и экспорта).
    /// </summary>
    public string SourceFile { get; set; } = string.Empty;
}