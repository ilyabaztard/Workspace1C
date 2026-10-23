using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Единый выбор учётных данных информационной базы по режиму (issue #236, #57).
/// «1С:Предприятие» использует отдельную авторизацию (<see cref="Infobase.EnterpriseAuth"/>),
/// если она задана; «Конфигуратор» — отдельную авторизацию (<see cref="Infobase.ConfiguratorAuth"/>),
/// если она задана; иначе — авторизацию информационной базы (<see cref="Infobase.Connection"/>,
/// обратная совместимость). При незаданной авторизации базы (режима и базы) подставляется
/// общая авторизация <see cref="AppSettings.CommonAuthorization"/> (issue #57) — fallback для
/// всех ИБ. Учётные данные Хранилища конфигурации разрешаются отдельно
/// (<see cref="ResolveRepository"/>) из <see cref="Infobase.Repository"/>.
/// Резолвинг применяется и при запуске 1С (<see cref="OneCLauncher"/>), и при чтении сведений
/// о конфигурации через COM-коннектор (<see cref="OneCComConnector"/>), чтобы учётные данные
/// выбирались одинаково: раздельная авторизация Конфигуратора/Предприятия не терялась при
/// построении строки подключения COM.
/// </summary>
public static class InfobaseAuthResolver
{
    /// <summary>
    /// Разрешает режим аутентификации, логин и пароль для заданного режима.
    /// Порядок приоритета:
    /// 1) раздельная авторизация режима — <see cref="Infobase.EnterpriseAuth"/> для
    ///    «1С:Предприятия» / <see cref="Infobase.ConfiguratorAuth"/> для «Конфигуратора»,
    ///    если она задана (не <see cref="InfobaseAuthSettings.IsDefault"/>);
    /// 2) авторизация информационной базы (<see cref="Infobase.Connection"/>), если она
    ///    задана явно (режим не «Запрос» или заполнены пользователь/пароль);
    /// 3) общая авторизация <paramref name="commonAuth"/> (issue #57), если задана;
    /// 4) иначе — «Запрос имени и пароля» с пустыми реквизитами (платформа спросит
    ///    имя и пароль при запуске).
    /// <para>
    /// ПОВЕДЕНЧЕСКИЙ НЮАНС (issue #57): ранее <c>EnterpriseAuth != null</c> использовался
    /// даже если значения дефолтные («Запрос» с пустыми реквизитами, <see cref="InfobaseAuthSettings.IsDefault"/>).
    /// Теперь «дефолтная» раздельная авторизация считается незаданной и даёт fallback на
    /// авторизацию базы, затем на общую. Для баз, где раздельная авторизация задана
    /// логином/паролем, поведение не меняется.
    /// </para>
    /// </summary>
    public static void Resolve(Infobase infobase, OneCLaunchMode mode,
        out AuthenticationMode authMode, out string authUser, out string authPassword,
        InfobaseAuthSettings? commonAuth = null)
    {
        // 1) Раздельная авторизация режима. «Дефолтные» настройки (IsDefault — «Запрос»
        // с пустыми реквизитами) считаются незаданными и переходят к следующим шагам.
        if (mode == OneCLaunchMode.Enterprise
            && infobase.EnterpriseAuth is { } entAuth && !entAuth.IsDefault)
        {
            authMode = entAuth.AuthenticationMode;
            authUser = entAuth.User;
            authPassword = entAuth.Password;
            return;
        }

        if (mode == OneCLaunchMode.Configurator
            && infobase.ConfiguratorAuth is { } cfgAuth && !cfgAuth.IsDefault)
        {
            authMode = cfgAuth.AuthenticationMode;
            authUser = cfgAuth.User;
            authPassword = cfgAuth.Password;
            return;
        }

        // 2) Авторизация информационной базы (Connection): используется, если задана явно
        // (режим не «Запрос» или заполнены пользователь/пароль). Дефолтная Connection
        // («Запрос» с пустыми реквизитами) считается незаданной.
        var conn = infobase.Connection;
        if (conn is not null && (conn.AuthenticationMode != AuthenticationMode.Prompt
            || !string.IsNullOrWhiteSpace(conn.User)
            || !string.IsNullOrWhiteSpace(conn.Password)))
        {
            authMode = conn.AuthenticationMode;
            authUser = conn.User ?? string.Empty;
            authPassword = conn.Password ?? string.Empty;
            return;
        }

        // 3) Общая авторизация для всех ИБ (issue #57): fallback, если у базы не задана
        // собственная авторизация (для режима или базы).
        if (commonAuth is { } a && !a.IsDefault)
        {
            authMode = a.AuthenticationMode;
            authUser = a.User;
            authPassword = a.Password;
            return;
        }

        // 4) Прежнее поведение: платформа запросит имя и пароль.
        authMode = AuthenticationMode.Prompt;
        authUser = string.Empty;
        authPassword = string.Empty;
    }

    /// <summary>
    /// Разрешает отдельные учётные данные Хранилища конфигурации (логин и пароль).
    /// В отличие от Предприятия/Конфигуратора у хранилища нет «базовой» авторизации:
    /// используются собственные реквизиты <see cref="Infobase.Repository"/> (поле ввода
    /// логина/пароля в свойствах базы), поэтому метод возвращает их напрямую. Применяется
    /// при запуске Конфигуратора для передачи ключей /ConfigurationRepositoryN и
    /// /ConfigurationRepositoryP.
    /// </summary>
    public static void ResolveRepository(Infobase infobase,
        out string repositoryUser, out string repositoryPassword)
    {
        var repo = infobase.Repository;
        repositoryUser = repo?.User ?? string.Empty;
        repositoryPassword = repo?.Password ?? string.Empty;
    }
}