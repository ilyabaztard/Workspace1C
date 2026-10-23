namespace Workspace1C.Services;

/// <summary>
/// Поле строки подключения, к которому применяется замена (0.3.9.187, функция 6
/// «Массовая замена в строке подключения баз»).
/// </summary>
public enum ConnectionField
{
    /// <summary>Хост кластера 1С (параметр <c>Srvr</c> без порта; IPv6-адрес сохраняется в квадратных скобках).</summary>
    Server,

    /// <summary>Порт кластера 1С (числовая часть <c>Srvr</c>, отдельно от хоста; 1541 — значение по умолчанию).</summary>
    Port,

    /// <summary>Имя информационной базы на сервере (параметр <c>Ref</c>).</summary>
    Ref,

    /// <summary>Путь файловой базы (параметр <c>File</c>).</summary>
    FilePath,

    /// <summary>URL веб-публикации (параметр <c>WS</c>).</summary>
    WebUrl,

    /// <summary>Вся строка подключения как текст (для подстроки/префикса/regex по всему тексту, включая неизвестные параметры).</summary>
    Any
}

/// <summary>
/// Режим сопоставления искомого текста со значением поля строки подключения.
/// </summary>
public enum ConnectionMatchMode
{
    /// <summary>Точное совпадение всего значения.</summary>
    Exact,

    /// <summary>Значение начинается с искомого текста.</summary>
    Prefix,

    /// <summary>Искомый текст встречается внутри значения.</summary>
    Substring,

    /// <summary>Искомый текст — это паттерн .NET <see cref="System.Text.RegularExpressions.Regex"/>.</summary>
    Regex
}

/// <summary>
/// Правило замены: «найти» → «заменить на» в выбранном поле строки подключения.
/// Для <see cref="ConnectionMatchMode.Exact"/>/<see cref="ConnectionMatchMode.Prefix"/>/
/// <see cref="ConnectionMatchMode.Substring"/> искомый текст трактуется буквально
/// (regex-метасимволы экранируются при поиске); для <see cref="ConnectionMatchMode.Regex"/>
/// — это паттерн .NET <see cref="System.Text.RegularExpressions.Regex"/>.
/// <paramref name="IgnoreCase"/> управляет учётом регистра. Замена всегда литеральная
/// (без интерпретации $-ссылок регулярных выражений).
/// </summary>
/// <param name="Find">Искомый текст (пустой — правило не применяется).</param>
/// <param name="Replace">Текст замены.</param>
/// <param name="Field">Поле строки подключения, к которому применяется замена.</param>
/// <param name="Mode">Режим сопоставления.</param>
/// <param name="IgnoreCase">true — сопоставлять без учёта регистра.</param>
public sealed record ConnectionStringReplaceRule(
    string Find,
    string Replace,
    ConnectionField Field,
    ConnectionMatchMode Mode,
    bool IgnoreCase);

/// <summary>
/// Правило смены версии платформы 1С (issue #54): массовое действие «Заменить строки
/// подключения и платформу…» — независимо от правил замены строк меняет
/// <see cref="Infobase.PlatformVersion"/> у кандидатов. Применяется и к базам без
/// строки подключения. Пустая версия — правило не применяется.
/// </summary>
/// <param name="Platform">Целевая версия платформы (например «8.3.27.1644»).</param>
public sealed record PlatformReplaceRule(string Platform);