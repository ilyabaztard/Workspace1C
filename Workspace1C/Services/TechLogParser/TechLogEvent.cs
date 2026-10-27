using System.Globalization;

namespace Workspace1C.Services.TechLogParser;

/// <summary>
/// Одно событие технологического журнала 1С (выпуск 1.0.0.86, п.4 «Анализ ТЖ»).
/// Строка журнала имеет вид
/// <c>20250115 143602.871-28472-17824 duration=110587 process=1cv8 usr=Иванов event=DBMSSQL DBMSSQL: SELECT ...</c>:
/// префикс даты/времени с идентификаторами процесса и потока, затем пары
/// <c>свойство=значение</c> и контекст события (все символы после <c>event=…</c>,
/// включая продолжение на следующих строках, пока не встретится новый префикс даты).
/// Длительность в технологическом журнале 8.3 — в микросекундах (шаблон
/// <see cref="TechLogService.BuildXml"/> умножает порог мс на 1000).
/// </summary>
public sealed class TechLogEvent
{
    /// <summary>Время события (локальное время сервера, без часового пояса).</summary>
    public DateTime Timestamp { get; init; }

    /// <summary>Код события: CALL, DBMSSQL, SDBL, TLOCK, TDEADLOCK, EXCP и др.</summary>
    public string EventType { get; init; } = string.Empty;

    /// <summary>Свойство <c>process</c> (например <c>1cv8</c>, <c>rphost</c>).</summary>
    public string Process { get; init; } = string.Empty;

    /// <summary>
    /// Информационная база/контекст события: значение свойств базы
    /// (<c>p:processName</c> клиента, <c>base</c>, <c>InfoBase</c>) или часть
    /// <c>process</c> после разделителя. Может быть пустой — тогда отбор по базе
    /// ищет путь к файлу/имя БД в процессе и контексте.
    /// </summary>
    public string Base { get; init; } = string.Empty;

    /// <summary>Пользователь (<c>usr</c>).</summary>
    public string User { get; init; } = string.Empty;

    /// <summary>Длительность события в микросекундах (0 — не указана).</summary>
    public long DurationUs { get; init; }

    /// <summary>
    /// Контекст события — весь текст после <c>event=…</c>, включая продолжение
    /// на следующих строках (текст SQL-запроса, описание исключения, объекты блокировки).
    /// </summary>
    public string Context { get; init; } = string.Empty;

    /// <summary>Все свойства строки (ключи без учёта регистра).</summary>
    public IReadOnlyDictionary<string, string> Properties { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Длительность в миллисекундах (для отображения и отчётов).</summary>
    public double DurationMs => DurationUs / 1000.0;

    /// <summary>Время в формате журнала «yyyyMMdd HHmmss.fff».</summary>
    public string TimeText => Timestamp.ToString("yyyyMMdd HHmmss.fff", CultureInfo.InvariantCulture);
}