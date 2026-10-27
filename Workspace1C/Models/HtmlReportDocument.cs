namespace Workspace1C.Models;

/// <summary>
/// Заметка базы в HTML-отчёте (п.8 «Заметки к базе», 1.0.0.90): markdown-текст карточки,
/// сконвертированный в HTML (экранирование + простые теги), имя базы и группа.
/// Чистый .NET без зависимостей — конвертация выполняется генератором
/// (<c>HtmlReportExporter.MarkdownToHtml</c>).
/// </summary>
public sealed class HtmlReportNoteRow
{
    /// <summary>Имя базы (заголовок секции заметки в отчёте).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Группа (полный путь/имя) для сортировки/навигации.</summary>
    public string GroupPath { get; init; } = string.Empty;

    /// <summary>Текст заметки, сконвертированный в HTML (безопасный, экранированный).</summary>
    public string MarkdownHtml { get; init; } = string.Empty;
}

/// <summary>
/// Строка таблицы HTML-отчёта по базам (0.3.9.131): все значения уже готовы к выводу
/// (тексты локализованы, размеры отформатированы) — генератор только экранирует их.
/// Данные собираются из существующих источников: <see cref="Infobase"/> (доступность,
/// строка подключения, теги, закладка) и строки «Центра обслуживания»
/// (<see cref="ViewModels.MaintenanceCenterRowViewModel"/> — размер, копия, версия,
/// признак проблемы). Чистый .NET без платформенных зависимостей.
/// </summary>
public sealed class HtmlReportRow
{
    /// <summary>Имя базы.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Группа (полный путь/имя), как в CSV-экспорте.</summary>
    public string GroupPath { get; init; } = string.Empty;

    /// <summary>Тип подключения: файловая / клиент-серверная / веб.</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>Строка подключения для отображения.</summary>
    public string ConnectionString { get; init; } = string.Empty;

    /// <summary>Доступность по последнему результату проверки.</summary>
    public bool IsAvailable { get; init; }

    /// <summary>Колонка «Доступность»: статус + время последней проверки (если была).</summary>
    public string AvailabilityText { get; init; } = string.Empty;

    /// <summary>Конфигурация / версия (известная или закэшированная; «—», если нет).</summary>
    public string Configuration { get; init; } = string.Empty;

    /// <summary>Размер ИБ в удобочитаемом виде («—», если неизвестен).</summary>
    public string Size { get; init; } = string.Empty;

    /// <summary>Дата последней копии («Никогда», если копии нет).</summary>
    public string LastBackup { get; init; } = string.Empty;

    /// <summary>Дата изменений файла ИБ (ГГГГ-ММ-ДД ЧЧ:ММ) или пусто.</summary>
    public string Modified { get; init; } = string.Empty;

    /// <summary>Теги через запятую.</summary>
    public string Tags { get; init; } = string.Empty;

    /// <summary>Номер закладки (1–9) или пусто.</summary>
    public string Favorite { get; init; } = string.Empty;

    /// <summary>
    /// True — база проблемная по критериям Центра обслуживания: недоступна / нет копии /
    /// копия старше 7 дней / кэш больше 1 ГБ / мало свободного места на диске /
    /// обнаружен новый релиз конфигурации. Строка подсвечивается цветом.
    /// </summary>
    public bool HasProblem { get; init; }
}

/// <summary>
/// Локализованные подписи HTML-отчёта (0.3.9.131). Собираются в MainViewModel из
/// LocalizationManager перед построением документа, чтобы генератор оставался чистым
/// (по образцу <c>CsvExporter</c>, который получает готовые заголовки колонок).
/// </summary>
public sealed class HtmlReportLabels
{
    /// <summary>Заголовок отчёта (h1).</summary>
    public string Header { get; init; } = string.Empty;

    /// <summary>Подпись карточки «Всего баз».</summary>
    public string SummaryTotal { get; init; } = string.Empty;

    /// <summary>Подпись карточки «Доступно».</summary>
    public string SummaryAvailable { get; init; } = string.Empty;

    /// <summary>Подпись карточки «Недоступно».</summary>
    public string SummaryUnavailable { get; init; } = string.Empty;

    /// <summary>Подпись карточки «Баз с копиями».</summary>
    public string SummaryWithBackups { get; init; } = string.Empty;

    /// <summary>Подпись карточки «Объём ИБ».</summary>
    public string SummarySize { get; init; } = string.Empty;

    /// <summary>Подпись карточки «Проблемных баз».</summary>
    public string SummaryProblems { get; init; } = string.Empty;

    /// <summary>Ссылка навигации «Все базы».</summary>
    public string ShowAll { get; init; } = string.Empty;

    /// <summary>Ссылка навигации «Только проблемы».</summary>
    public string OnlyProblems { get; init; } = string.Empty;

    /// <summary>Подпись «По группам:» перед ссылками-якорями секций.</summary>
    public string GroupNav { get; init; } = string.Empty;

    /// <summary>Легенда подсветки проблемных строк.</summary>
    public string Legend { get; init; } = string.Empty;

    /// <summary>Колонка «Имя базы».</summary>
    public string ColName { get; init; } = string.Empty;

    /// <summary>Колонка «Группа».</summary>
    public string ColGroup { get; init; } = string.Empty;

    /// <summary>Колонка «Тип».</summary>
    public string ColType { get; init; } = string.Empty;

    /// <summary>Колонка «Строка подключения».</summary>
    public string ColConnection { get; init; } = string.Empty;

    /// <summary>Колонка «Доступность».</summary>
    public string ColAvailability { get; init; } = string.Empty;

    /// <summary>Колонка «Конфигурация / версия».</summary>
    public string ColConfiguration { get; init; } = string.Empty;

    /// <summary>Колонка «Размер ИБ».</summary>
    public string ColSize { get; init; } = string.Empty;

    /// <summary>Колонка «Последняя копия».</summary>
    public string ColLastBackup { get; init; } = string.Empty;

    /// <summary>Колонка «Дата изменений».</summary>
    public string ColModified { get; init; } = string.Empty;

    /// <summary>Колонка «Теги».</summary>
    public string ColTags { get; init; } = string.Empty;

    /// <summary>Колонка «Закладка».</summary>
    public string ColFavorite { get; init; } = string.Empty;

    /// <summary>Статус «Доступна».</summary>
    public string Available { get; init; } = string.Empty;

    /// <summary>Статус «Недоступна».</summary>
    public string Unavailable { get; init; } = string.Empty;

    /// <summary>Заголовок секции «Заметки» (с числом): формат подписи, {0} — количество.</summary>
    public string NotesHeaderFormat { get; init; } = string.Empty;

    /// <summary>Текст «Заметок нет» для секции «Заметки» в отчёте.</summary>
    public string NotesEmpty { get; init; } = string.Empty;
}

/// <summary>
/// Документ HTML-отчёта по базам (0.3.9.131): шапка (приложение, дата/время, профиль),
/// сводка по последним известным данным и строки таблицы. Потребляется генератором
/// <c>HtmlReportExporter</c>. Чистый .NET — юнит-тесты строят документ напрямую.
/// </summary>
public sealed class HtmlReportDocument
{
    /// <summary>Название приложения (для шапки и title).</summary>
    public string AppName { get; init; } = string.Empty;

    /// <summary>Дата/время формирования (готовый текст).</summary>
    public string GeneratedAtText { get; init; } = string.Empty;

    /// <summary>Имя активного профиля («-», если профиль не выбран).</summary>
    public string ProfileName { get; init; } = string.Empty;

    /// <summary>Всего баз в отчёте.</summary>
    public int TotalCount { get; init; }

    /// <summary>Доступно по последним результатам проверки.</summary>
    public int AvailableCount { get; init; }

    /// <summary>Недоступно по последним результатам проверки.</summary>
    public int UnavailableCount { get; init; }

    /// <summary>Баз с известной копией (LastBackupUtc заполнен).</summary>
    public int WithBackupsCount { get; init; }

    /// <summary>Сумма размеров ИБ в байтах (по известным размерам; 0 — неизвестно).</summary>
    public long TotalSizeBytes { get; init; }

    /// <summary>Количество проблемных баз (критерии Центра обслуживания).</summary>
    public int ProblemCount { get; init; }

    /// <summary>Локализованные подписи документа.</summary>
    public HtmlReportLabels Labels { get; init; } = new();

    /// <summary>Строки таблицы (видимые базы, приватные скрыты как в CSV-экспорте).</summary>
    public IReadOnlyList<HtmlReportRow> Rows { get; init; } = Array.Empty<HtmlReportRow>();

    /// <summary>
    /// Заметки баз для секции «Заметки» (п.8, 1.0.0.90): базы с непустой markdown-карточкой,
    /// отсортированные по группе/имени. Пусто — секция не выводится в отчёте.
    /// </summary>
    public IReadOnlyList<HtmlReportNoteRow> NoteRows { get; init; } = Array.Empty<HtmlReportNoteRow>();
}
