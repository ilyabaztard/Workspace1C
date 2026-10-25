using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Чистая подстановка параметров сценариев запуска скриптов и пользовательских
/// действий (issue #308, функция 7).
/// <para>
/// Поддерживаются токены двух синтаксисов — <c>%ключ%</c> и <c>{ключ}</c> (например
/// <c>{ИмяБазы}</c>, <c>{СтрокаПодключения}</c>): имя базы (<c>%name%</c>/<c>{ИмяБазы}</c>),
/// свойства подключения через точку (<c>%connection.server%</c>, <c>%connection.database%</c>,
/// <c>%connection.filePath%</c>, <c>%connection.password%</c> и т.д.), пароль — также
/// плоский ключ <c>%password%</c>/<c>{Пароль}</c>, и текущая дата — <c>%date%</c>/<c>{Дата}</c>
/// (по умолчанию <c>yyyy-MM-dd</c>) либо <c>%date:Формат%</c>/<c>{Дата:Формат}</c>
/// (формат .NET, например <c>%date:yyyyMMdd_HHmm%</c>). Произвольные поля базы —
/// <c>%Additional_N%</c>/<c>{Additional_N}</c> (issue #6): значение N-й строки таблицы
/// «Произвольные поля» вкладки «Идентификатор» (ключи <c>additional_1..N</c>).
/// Неизвестный ключ по умолчанию остаётся в строке как есть; при
/// <c>leaveUnknown=false</c> заменяется пустой строкой.
/// </para>
/// <para>
/// Карта значений собирается из явных ключей плюс динамического прохода рефлексией
/// по публичным свойствам <see cref="Infobase"/> и <see cref="ConnectionSettings"/>
/// (ключи <c>connection.<имя></c> и плоские <c><имя></c> в нижнем регистре):
/// будущие свойства моделей подхватываются автоматически. Явные ключи (включая
/// русские алиасы <c>{ИмяБазы}</c>, <c>{СтрокаПодключения}</c>, <c>{Каталог}</c>,
/// <c>{ПутьИБ}</c>, <c>{Тип}</c>, <c>{Сервер}</c>, <c>{ИмяНаСервере}</c>,
/// <c>{ИмяГруппы}</c>, <c>{Пользователь}</c>, <c>{Пароль}</c>, <c>{ВебURL}</c>,
/// <c>{Порт}</c>) перезаписывают динамические; все ключи регистронезависимы.
/// </para>
/// <para>
/// Для пользовательских действий значения могут экранироваться под выбранный shell
/// (<see cref="EscapeForShell"/>, флаг <c>escapeValues</c> в <see cref="Resolve"/>);
/// сборка команды действия — <see cref="BuildActionCommandLine"/> /
/// <see cref="BuildActionShellCommandLine"/>. Класс не зависит от UI-платформы и
/// покрыт юнит-тестами (ScriptParameterResolverTests).
/// </para>
/// </summary>
public static class ScriptParameterResolver
{
    // Составной паттерн: прежние токены %ключ% (группа 2) и новые {ключ} (группа 3).
    // Группа 1 — весь совпавший токен (для leaveUnknown возвращается как есть).
    private static readonly Regex TokenPattern = new("(%([^%]+)%|\\{([^{}]+)\\})", RegexOptions.Compiled);

    /// <summary>Формат даты по умолчанию для токена <c>%date%</c>.</summary>
    public const string DefaultDateFormat = "yyyy-MM-dd";

    // Кэшированный список публичных свойств для рефлексии (не читать GetProperties
    // на каждый вызов; значения скалярных типов — строки и значимые типы).
    private static readonly IReadOnlyList<PropertyInfo> InfobaseSubstitutionProperties =
        typeof(Infobase).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(IsScalarProperty)
            .ToArray();

    private static readonly IReadOnlyList<PropertyInfo> ConnectionSubstitutionProperties =
        typeof(ConnectionSettings).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(IsScalarProperty)
            .ToArray();

    /// <summary>Отбирает свойства со скалярными значениями (строки и значимые типы),
    /// исключая ссылочные объекты (Connection, Repository, списки и т.д.).</summary>
    private static bool IsScalarProperty(PropertyInfo property)
    {
        var type = property.PropertyType;
        return type == typeof(string)
            || type.IsEnum
            || type.IsValueType;
    }

    /// <summary>
    /// Собирает словарь значений подстановок для информационной базы: плоские ключи
    /// (<c>name</c>, <c>password</c>) и свойства подключения с префиксом <c>connection.</c>.
    /// Публичные свойства <see cref="Infobase"/> и <see cref="ConnectionSettings"/>
    /// попадают в карту динамически (в нижнем регистре), явные ключи — поверх.
    /// </summary>
    /// <param name="infobase">База (может быть null — пустая карта).</param>
    /// <param name="resolvedCredentials">
    /// Логин/пароль, решённые для реального запуска (issue #57: общая авторизация базы —
    /// <c>InfobaseAuthResolver</c>). Подставляются в ключи логина/пароля только когда
    /// у самой базы собственных учётных данных нет (пусто) — сценарий «понимает все
    /// свойства базы» в том виде, в котором база реально запускается (issue #67).
    /// </param>
    public static Dictionary<string, string> BuildValueMap(Infobase? infobase,
        (string? User, string? Password)? resolvedCredentials = null)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (infobase is null)
            return map;

        // Динамика по Infobase: плоские ключи <имя> для всех публичных скалярных свойств.
        foreach (var prop in InfobaseSubstitutionProperties)
        {
            var value = ReadPropertyValue(infobase, prop);
            if (value is not null)
                map[prop.Name.ToLowerInvariant()] = value;
        }

        var conn = infobase.Connection;
        if (conn is null)
            return map;

        // Динамика по ConnectionSettings: плоские ключи <имя> и connection.<имя>.
        foreach (var prop in ConnectionSubstitutionProperties)
        {
            var value = ReadPropertyValue(conn, prop);
            if (value is null)
                continue;
            var key = prop.Name.ToLowerInvariant();
            map[key] = value;
            map["connection." + key] = value;
        }

        // Явные ключи: каноничные имена токенов, перезаписывают динамические.
        map["name"] = infobase.Name ?? "";
        map["connection.type"] = conn.Type.ToString();
        map["connection.server"] = conn.Server ?? "";
        map["connection.serverPort"] = conn.GetServerWithPort();
        map["connection.database"] = conn.DatabaseName ?? "";
        map["connection.filePath"] = conn.FilePath ?? "";
        map["connection.webUrl"] = conn.WebUrl ?? "";
        map["connection.port"] = conn.Port.ToString();
        map["connection.connectionString"] = conn.ToConnectionString();
        map["connection.user"] = conn.User ?? "";
        map["connection.password"] = conn.Password ?? "";
        map["password"] = conn.Password ?? "";
        map["connection.blockScheduledJobs"] = conn.BlockScheduledJobs.ToString();
        map["connection.forbidSpeechRecognition"] = conn.ForbidSpeechRecognition.ToString();
        map["connection.authenticationMode"] = conn.AuthenticationMode.ToString();
        map["connection.useOsAuthentication"] = conn.UseOsAuthentication.ToString();

        // Русские и канонические алиасы (цикл 0.3.9.194, функция 7): ключи
        // регистронезависимы (словарь OrdinalIgnoreCase), перезаписывают динамические
        // и прежние явные ключи; {ПутьИБ} — синоним {Каталог} (путь файловой базы).
        map["имябазы"] = infobase.Name ?? "";
        map["строкаподключения"] = conn.ToConnectionString();
        map["каталог"] = conn.FilePath ?? "";
        map["путьиб"] = conn.FilePath ?? "";
        map["id"] = infobase.Id ?? "";
        map["тип"] = conn.Type.ToString();
        map["сервер"] = conn.Server ?? "";
        map["имянасервере"] = conn.DatabaseName ?? "";
        map["имягруппы"] = infobase.Group ?? "";
        map["пользователь"] = conn.User ?? "";
        map["пароль"] = conn.Password ?? "";
        map["вебurl"] = conn.WebUrl ?? "";
        map["порт"] = conn.Port.ToString();

        // Произвольные поля базы (issue #6): ключи additional_1..N по номеру строки
        // таблицы «Произвольные поля» (вкладка «Идентификатор»). Токен %Additional_N%
        // регистронезависим (словарь OrdinalIgnoreCase); номер за пределами списка
        // остаётся в строке как есть (leaveUnknown) — корректность на откуп пользователя.
        var additional = infobase.AdditionalFields;
        if (additional is { Count: > 0 })
        {
            for (var i = 0; i < additional.Count; i++)
                map["additional_" + (i + 1)] = additional[i] ?? "";
        }

        // Логин/пароль при общей авторизации (issue #67, продолжение #57): собственных
        // учётных данных у базы нет (пусто) — подставляем решённые для реального запуска
        // («Договаривались на старте, что будет понимать ВСЕ свойства базы… подставить
        // логин оно пока не может»). Собственные данные не перезаписываются.
        if (conn is not null && resolvedCredentials is { } creds)
        {
            if (string.IsNullOrWhiteSpace(conn.User) && !string.IsNullOrWhiteSpace(creds.User))
            {
                map["connection.user"] = creds.User;
                map["пользователь"] = creds.User;
            }
            if (string.IsNullOrWhiteSpace(conn.Password) && !string.IsNullOrWhiteSpace(creds.Password))
            {
                map["connection.password"] = creds.Password;
                map["пароль"] = creds.Password;
                map["password"] = creds.Password;
            }
        }
        return map;
    }

    /// <summary>
    /// Читает значение свойства как строку (инвариантная культура); при ошибке
    /// доступа пропускает ключ — не роняет построение карты.
    /// </summary>
    private static string? ReadPropertyValue(object target, PropertyInfo property)
    {
        try
        {
            var value = property.GetValue(target);
            if (value is null)
                return null;
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            // Значение свойства недоступно — ключ просто не попадёт в карту.
            return null;
        }
    }

    /// <summary>
    /// Выполняет подстановку токенов в шаблоне. Момент времени <paramref name="now"/>
    /// передаётся явно для тестируемости; <c>null</c> — текущее время.
    /// Оба синтаксиса токенов (<c>%…%</c> и <c>{…}</c>) обрабатываются одинаково.
    /// </summary>
    /// <param name="template">Шаблон с токенами <c>%…%</c> или <c>{…}</c>.</param>
    /// <param name="values">Значения подстановок (см. <see cref="BuildValueMap"/>).</param>
    /// <param name="now">Момент времени для <c>%date%</c>/<c>%date:…%</c>.</param>
    /// <param name="leaveUnknown">Оставлять неизвестный токен как есть (<c>true</c>)
    /// или заменять пустой строкой (<c>false</c>).</param>
    /// <param name="escapeValues">Экранировать подставленные значения из словаря
    /// через <see cref="EscapeForShell"/> (<c>true</c>); токены даты не экранируются.
    /// По умолчанию <c>false</c> — прежнее поведение сценариев.</param>
    /// <param name="shell">Интерпретатор для экранирования; <c>null</c> —
    /// <see cref="ScriptShell.Auto"/> (по платформе). Используется только при
    /// <paramref name="escapeValues"/> = <c>true</c>.</param>
    public static string Resolve(
        string? template,
        IReadOnlyDictionary<string, string>? values,
        DateTime? now = null,
        bool leaveUnknown = true,
        bool escapeValues = false,
        ScriptShell? shell = null)
    {
        if (string.IsNullOrEmpty(template))
            return template ?? "";

        var current = now ?? DateTime.Now;
        return TokenPattern.Replace(template, match =>
        {
            var token = (match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value).Trim();
            // Дата распознаётся и по-английски (%date%), и по-русски ({Дата}) —
            // префикс формата отделяется двоеточием (%date:…%/{Дата:…}).
            if (token.Equals("date", StringComparison.OrdinalIgnoreCase)
                || token.Equals("дата", StringComparison.OrdinalIgnoreCase))
                return SafeFormat(current, DefaultDateFormat);
            if (token.StartsWith("date:", StringComparison.OrdinalIgnoreCase)
                || token.StartsWith("дата:", StringComparison.OrdinalIgnoreCase))
                return SafeFormat(current, token.Substring(token.IndexOf(':') + 1));

            if (values is not null && values.TryGetValue(token, out var value))
            {
                var resolved = value ?? "";
                return escapeValues ? EscapeForShell(resolved, shell ?? ScriptShell.Auto) : resolved;
            }

            return leaveUnknown ? match.Value : "";
        });
    }

    /// <summary>
    /// Собирает полную командную строку сценария: рабочая папка запуска (если задана,
    /// префикс <c>cd "…"</c> с разделителем), путь к файлу (в кавычках, если содержит
    /// пробелы) и резолвнутые параметры. Строка передаётся системному shell через
    /// <see cref="ExternalCommandRunner.BuildShellCommand"/>.
    /// <para>
    /// Разделитель между префиксом <c>cd</c> и командой зависит от интерпретатора:
    /// <c>cmd</c>/<c>sh</c> — <c>&&</c>, PowerShell — <c>;</c> (в powershell.exe 5.1
    /// лексема <c>&&</c> недопустима, issue #308, замечание @7OH). <see cref="ScriptShell.Auto"/>
    /// разрешается по <paramref name="isWindows"/> (cmd на Windows, sh на Linux — оба «&&»).
    /// </para>
    /// <para>
    /// Реальный запуск использует <see cref="System.Diagnostics.ProcessStartInfo.WorkingDirectory"/>
    /// (без <c>cd</c>); префикс <c>cd</c> нужен только для наглядного превью в окнах
    /// редактирования и выбора (issue #308, п.7). При пустой рабочей папке строка
    /// не меняется — старые сценарии мигрируют без изменения поведения.
    /// </para>
    /// </summary>
    /// <param name="scenario">Сценарий.</param>
    /// <param name="values">Значения подстановок (см. <see cref="BuildValueMap"/>).</param>
    /// <param name="now">Момент времени для токенов даты.</param>
    /// <param name="shell">Интерпретатор для выбора разделителя <c>cd</c>;
    /// <c>null</c> — <see cref="ScriptShell.Auto"/> (по платформе).</param>
    /// <param name="isWindows"><c>true</c> — платформа Windows; <c>null</c> — текущая ОС
    /// (используется только при <see cref="ScriptShell.Auto"/>).</param>
    public static string BuildCommandLine(
        ScriptScenario scenario,
        IReadOnlyDictionary<string, string>? values,
        DateTime? now = null,
        ScriptShell? shell = null,
        bool? isWindows = null,
        bool keepOpen = false)
    {
        if (scenario is null)
            return "";

        var path = (scenario.FilePath ?? "").Trim();
        var parts = new List<string>();

        // Папка запуска (issue #308, п.7): видна в превью как «cd "…" && …» для
        // cmd/sh и «cd "…"; …» для PowerShell — разделитель по выбранному шеллу.
        var workingDirectory = (scenario.WorkingDirectory ?? "").Trim();
        if (workingDirectory.Length > 0)
        {
            parts.Add("cd " + QuoteIfNeeded(workingDirectory));
            parts.Add(CommandSeparator(shell ?? ScriptShell.Auto, isWindows ?? OperatingSystem.IsWindows()));
        }

        if (path.Length > 0)
            parts.Add(QuoteIfNeeded(path));

        foreach (var parameter in scenario.Parameters ?? new List<string>())
        {
            var resolved = Resolve(parameter, values, now).Trim();
            if (resolved.Length > 0)
                parts.Add(resolved);
        }

        // «Не закрывать окно» (issue #308): хвостовая команда удержания окна —
        // pause (cmd), Read-Host (PowerShell), read (sh). Разделитель для cmd — «&»
        // (безусловное выполнение), чтобы окно удерживалось и при ошибке скрипта;
        // для PowerShell/sh — «;» (так же безусловно).
        var shouldKeepOpen = keepOpen || scenario.KeepOpen;
        if (shouldKeepOpen)
        {
            var tail = KeepOpenTail(shell ?? ScriptShell.Auto, isWindows ?? OperatingSystem.IsWindows());
            if (tail.Length > 0)
            {
                parts.Add(parts.Count > 0
                    ? KeepOpenSeparator(shell ?? ScriptShell.Auto, isWindows ?? OperatingSystem.IsWindows()) + " " + tail
                    : tail);
            }
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Хвостовая команда удержания консольного окна (issue #308, флаг «Не закрывать»):
    /// <c>cmd</c> — <c>pause</c>, <c>PowerShell</c> — <c>Read-Host</c>, <c>sh</c> — <c>read</c>.
    /// <see cref="ScriptShell.Auto"/> разрешается по платформе (<paramref name="isWindows"/>:
    /// Windows — cmd, Linux — sh). Пустая команда невозможна — все ветки возвращают текст.
    /// </summary>
    public static string KeepOpenTail(ScriptShell shell, bool isWindows)
    {
        var resolved = shell == ScriptShell.Auto ? (isWindows ? ScriptShell.Cmd : ScriptShell.Sh) : shell;
        return resolved switch
        {
            ScriptShell.PowerShell => "Read-Host",
            ScriptShell.Sh => "read",
            _ => "pause"
        };
    }

    /// <summary>
    /// Разделитель перед хвостовой командой удержания: для <c>cmd</c> — <c>&</c>
    /// (выполняется независимо от кода возврата — окно удерживается и при ошибке),
    /// для <c>PowerShell</c>/<c>sh</c> — <c>;</c> (то же безусловное выполнение).
    /// </summary>
    private static string KeepOpenSeparator(ScriptShell shell, bool isWindows)
    {
        var resolved = shell == ScriptShell.Auto ? (isWindows ? ScriptShell.Cmd : ScriptShell.Sh) : shell;
        return resolved == ScriptShell.PowerShell || resolved == ScriptShell.Sh ? ";" : "&";
    }

    /// <summary>
    /// Полная командная строка сценария с обёрткой выбранного интерпретатора
    /// (issue #308, п.9): тело <see cref="BuildCommandLine"/> оборачивается как
    /// <c>cmd.exe /c …</c>, <c>powershell -NoProfile -Command …</c> или
    /// <c>/bin/sh -c …</c>; при <see cref="ScriptShell.Auto"/> — по платформе
    /// (<paramref name="isWindows"/>). Используется в превью окон редактора/выбора
    /// и в логе запуска; реальный запуск выполняет ту же обёртку через
    /// <see cref="ExternalCommandRunner.RunDetached"/> с <c>scenario.Shell</c>.
    /// Шелл сценария передаётся в тело — разделитель <c>cd</c> соответствует
    /// интерпретатору (PowerShell — «;», issue #308, замечание @7OH).
    /// </summary>
    /// <param name="scenario">Сценарий.</param>
    /// <param name="values">Значения подстановок (см. <see cref="BuildValueMap"/>).</param>
    /// <param name="now">Момент времени для токенов даты.</param>
    /// <param name="isWindows"><c>true</c> — платформа Windows, <c>false</c> — Linux;
    /// <c>null</c> — определяется по текущей ОС (используется в рантайме).</param>
    public static string BuildShellCommandLine(
        ScriptScenario scenario,
        IReadOnlyDictionary<string, string>? values,
        DateTime? now = null,
        bool? isWindows = null)
    {
        if (scenario is null)
            return "";

        var shell = scenario.Shell;
        var body = BuildCommandLine(scenario, values, now, shell, isWindows);
        var (fileName, arguments) = ExternalCommandRunner.ResolveShellWrapper(
            shell,
            body,
            isWindows ?? OperatingSystem.IsWindows());
        return fileName + " " + arguments;
    }

    /// <summary>
    /// Экранирует значение для безопасной вставки в команду выбранного shell:
    /// <c>Cmd</c> — двойные кавычки и удвоение внутренних <c>"</c> (<c>""</c>);
    /// <c>Sh</c> — одинарные кавычки, <c>'</c> внутри → <c>'\''</c>;
    /// <c>PowerShell</c> — одинарные кавычки, <c>'</c> внутри → <c>''</c>;
    /// <c>Auto</c> — по платформе (<paramref name="isWindows"/> или текущая ОС).
    /// Пустое значение → пустая строка (без кавычек).
    /// </summary>
    /// <param name="value">Значение для экранирования.</param>
    /// <param name="shell">Интерпретатор; <c>Auto</c> — по платформе.</param>
    /// <param name="isWindows"><c>true</c> — платформа Windows; <c>null</c> — текущая ОС
    /// (используется только при <see cref="ScriptShell.Auto"/>).</param>
    public static string EscapeForShell(string? value, ScriptShell shell, bool? isWindows = null)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        var resolved = shell == ScriptShell.Auto
            ? ((isWindows ?? OperatingSystem.IsWindows()) ? ScriptShell.Cmd : ScriptShell.Sh)
            : shell;

        return resolved switch
        {
            ScriptShell.Cmd => "\"" + value.Replace("\"", "\"\"") + "\"",
            ScriptShell.Sh => "'" + value.Replace("'", "'\\''") + "'",
            ScriptShell.PowerShell => "'" + value.Replace("'", "''") + "'",
            _ => value
        };
    }

    /// <summary>
    /// Тело команды действия: подстановка токенов команды <see cref="CustomAction.Command"/>
    /// по карте значений базы (<see cref="BuildValueMap"/>) с учётом флага
    /// <see cref="CustomAction.EscapeValues"/> и выбранного интерпретатора
    /// <see cref="CustomAction.Shell"/>. Неизвестный токен остаётся как есть
    /// (leaveUnknown: true). Реальный запуск выполняет тело через
    /// <see cref="ExternalCommandRunner.RunAsync"/> без повторной обёртки.
    /// </summary>
    public static string BuildActionCommandLine(CustomAction action, Infobase? infobase, DateTime? now = null)
    {
        if (action is null)
            return "";
        return Resolve(
            action.Command,
            BuildValueMap(infobase),
            now,
            leaveUnknown: true,
            escapeValues: action.EscapeValues,
            shell: action.Shell);
    }

    /// <summary>
    /// Полная командная строка действия с обёрткой выбранного интерпретатора
    /// (для превью и лога): тело <see cref="BuildActionCommandLine"/> оборачивается
    /// как <c>cmd.exe /c …</c>, <c>powershell -NoProfile -Command …</c> или
    /// <c>/bin/sh -c …</c> через <see cref="ExternalCommandRunner.ResolveShellWrapper"/>;
    /// при <see cref="ScriptShell.Auto"/> — по платформе (<paramref name="isWindows"/>).
    /// </summary>
    public static string BuildActionShellCommandLine(
        CustomAction action, Infobase? infobase, DateTime? now = null, bool? isWindows = null)
    {
        if (action is null)
            return "";
        var body = BuildActionCommandLine(action, infobase, now);
        var (fileName, arguments) = ExternalCommandRunner.ResolveShellWrapper(
            action.Shell, body, isWindows ?? OperatingSystem.IsWindows());
        return fileName + " " + arguments;
    }

    /// <summary>
    /// Разделитель между префиксом <c>cd "…"</c> и командой: для PowerShell — <c>;</c>,
    /// для cmd/sh — <c>&&</c> (прежнее поведение). <see cref="ScriptShell.Auto"/>
    /// разрешается по платформе: cmd на Windows, sh на Linux — оба используют «&&».
    /// </summary>
    private static string CommandSeparator(ScriptShell shell, bool isWindows)
    {
        var resolved = shell == ScriptShell.Auto
            ? (isWindows ? ScriptShell.Cmd : ScriptShell.Sh)
            : shell;
        return resolved == ScriptShell.PowerShell ? ";" : "&&";
    }

    /// <summary>Пытается преобразовать строку в число параметров; некорректное — 0.</summary>
    private static string SafeFormat(DateTime now, string format)
    {
        try
        {
            return now.ToString(format);
        }
        catch (FormatException)
        {
            // Некорректный формат пользователя не должен ронять подстановку.
            return now.ToString(DefaultDateFormat);
        }
    }

    /// <summary>Заключает значение в кавычки, если оно содержит пробелы и ещё не в кавычках.</summary>
    private static string QuoteIfNeeded(string value)
        => value.Contains(' ') && !value.StartsWith("\"") ? "\"" + value + "\"" : value;
}