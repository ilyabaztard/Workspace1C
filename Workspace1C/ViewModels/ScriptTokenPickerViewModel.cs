using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// Строка диалога выбора переменной подстановки сценария (issue #67): итоговое имя
/// переменной (токен %…%), значение для выбранной базы (для превью; пароль маскируется),
/// человекочитаемое описание (что означает значение и откуда берётся), секция
/// («Основные»/«Подключение»/…) и признак начала секции для заголовков в списке.
/// </summary>
public sealed class ScriptTokenOption
{
    public ScriptTokenOption(string token, string value, string? sectionKey = null,
        bool sectionStart = false, string? description = null)
    {
        Token = token;
        Value = value;
        SectionKey = sectionKey;
        SectionStart = sectionStart;
        Description = description;
    }

    /// <summary>Итоговое имя переменной (токен вида <c>%connection.server%</c>).</summary>
    public string Token { get; }

    /// <summary>Значение для выбранной базы (может быть пустым).</summary>
    public string Value { get; }

    /// <summary>Ключ локализации секции («Основные», «Подключение», …); null — без секции.</summary>
    public string? SectionKey { get; }

    /// <summary>Признак первой строки секции — перед ней показывается заголовок.</summary>
    public bool SectionStart { get; }

    /// <summary>
    /// Человекочитаемое описание значения и его происхождения (issue #67: «нужны пояснения» —
    /// почему <c>Connection.User</c> пуст в файле баз, а справа «администратор», и откуда берётся
    /// раздельная авторизация). Для неизвестных динамических полей — <c>null</c> (показывается
    /// универсальная подпись).
    /// </summary>
    public string? Description { get; }

    /// <summary>
    /// Описание для отображения: при отсутствии — обобщённая подпись «поле базы из файла
    /// настроек» (Script.TokenDescriptionFallback), чтобы у каждого ключа была подпись.
    /// </summary>
    public string DisplayDescription => string.IsNullOrEmpty(Description)
        ? LocalizationManager.T("Script.TokenDescriptionFallback")
        : Description;

    /// <summary>Локализованное имя секции (заголовок группы в диалоге).</summary>
    public string SectionDisplay => string.IsNullOrEmpty(SectionKey) ? string.Empty : LocalizationManager.T(SectionKey);

    /// <summary>Значение для отображения: пустое — «—»; токены пароля маскируются.</summary>
    public string DisplayValue
    {
        get
        {
            if (string.IsNullOrEmpty(Value))
                return LocalizationManager.T("Script.TokenValueEmpty");
            return IsSecret ? "••••••" : Value;
        }
    }

    /// <summary>Токен пароля — значение маскируется в диалоге (логин/пользователь не секрет).</summary>
    public bool IsSecret =>
        Token.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0
        || Token.IndexOf("пароль", StringComparison.OrdinalIgnoreCase) >= 0;
}

/// <summary>
/// ViewModel окна «Выбор переменной» для подстановок сценариев (issue #67): список всех
/// переменных базы («итоговое имя переменной — значение для базы»), сгруппированный по
/// секциям («Основные»/«Подключение»/«Произвольные поля»/«Платформа»/«Дата»), поиск по
/// обеим колонкам, выбор по «Ок» и двойному клику. Чистый .NET — обе платформы.
/// </summary>
public sealed class ScriptTokenPickerViewModel : ViewModelBase
{
    // Ключи локализации секций диалога (issue #67 — «список доступных полей = структура базы»).
    private const string SectionMain = "Script.TokenSectionMain";
    private const string SectionConnection = "Script.TokenSectionConnection";
    private const string SectionAuth = "Script.TokenSectionAuth";
    private const string SectionExtra = "Script.TokenSectionExtra";
    private const string SectionPlatform = "Script.TokenSectionPlatform";
    private const string SectionDate = "Script.TokenSectionDate";

    private readonly IReadOnlyList<ScriptTokenOption> _all;
    private string _query = string.Empty;
    private ScriptTokenOption? _selectedItem;

    public ScriptTokenPickerViewModel(Infobase? infobase)
    {
        _all = BuildOptions(infobase);
        Items = new ObservableCollection<ScriptTokenOption>();
        ApplyFilter();
        SelectedItem = Items.FirstOrDefault();
    }

    /// <summary>Отфильтрованный список переменных (с флагами начала секций).</summary>
    public ObservableCollection<ScriptTokenOption> Items { get; }

    /// <summary>Строка поиска — фильтрует по имени и значению (issue #67).</summary>
    public string Query
    {
        get => _query;
        set
        {
            if (SetProperty(ref _query, value ?? string.Empty))
                ApplyFilter();
        }
    }

    /// <summary>Выбранная строка.</summary>
    public ScriptTokenOption? SelectedItem
    {
        get => _selectedItem;
        set => SetProperty(ref _selectedItem, value);
    }

    /// <summary>Итоговый токен выбора (null — диалог отменён / ничего не выбрано).</summary>
    public string? SelectedToken => SelectedItem?.Token;

    /// <summary>Завершён ли диалог кнопкой «Ок».</summary>
    public bool Accepted { get; private set; }

    /// <summary>Фиксирует выбор («Ок») и возвращает токен.</summary>
    public string? ConfirmSelection()
    {
        Accepted = true;
        return SelectedToken;
    }

    /// <summary>
    /// Фильтр строк по запросу (регистронезависимо; имя, значение ИЛИ описание — issue #67).
    /// Порядок исходного списка сохраняется; флаги начала секций пересчитываются, чтобы
    /// после фильтрации заголовки секций оставались корректными (первая строка каждой
    /// секции — с флагом). Описание сохраняется в отфильтрованных строках.
    /// </summary>
    public static IReadOnlyList<ScriptTokenOption> FilterOptions(
        IReadOnlyList<ScriptTokenOption> options, string? query)
    {
        var q = (query ?? string.Empty).Trim();
        IEnumerable<ScriptTokenOption> source = options;
        if (q.Length > 0)
            source = options.Where(o => o.Token.Contains(q, StringComparison.OrdinalIgnoreCase)
                || o.Value.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (o.Description ?? string.Empty).Contains(q, StringComparison.OrdinalIgnoreCase));

        var result = new List<ScriptTokenOption>();
        string? currentSection = null;
        foreach (var option in source)
        {
            var section = option.SectionKey;
            var isStart = section is not null && !string.Equals(section, currentSection, StringComparison.Ordinal);
            result.Add(new ScriptTokenOption(option.Token, option.Value, section, isStart, option.Description));
            if (section is not null)
                currentSection = section;
        }

        return result;
    }

    private void ApplyFilter()
    {
        Items.Clear();
        foreach (var option in FilterOptions(_all, Query))
            Items.Add(option);
        SelectedItem = Items.FirstOrDefault();
    }

    /// <summary>
    /// Строит полный список переменных сценария для базы: статический справочник окна + все
    /// динамические ключи карты <see cref="ScriptParameterResolver.BuildValueMap"/> (рефлексия
    /// «понимает все реквизиты базы» — список полей 1-в-1 с картой, issue #67). Решённая
    /// авторизация (общая, issue #57) учитывается для логина/пароля; раздельные авторизации
    /// (<c>enterpriseauth.*</c>/<c>configuratorauth.*</c>) попадают в секцию «Авторизация»;
    /// контекст платформы (<c>%full_launch_*%</c>, <c>%full_exe_name%</c>, <c>%platform_version%</c>)
    /// добавляется в секцию «Платформа». У каждого ключа — человекочитаемое описание:
    /// статические токены берут его из <see cref="ScriptScenarioEditViewModel.AvailableTokens"/>
    /// (ключ локализации), динамические — из каталога описаний (или универсальная подпись).
    /// Итоговый список упорядочен по секциям: «Основные», «Подключение», «Авторизация»,
    /// «Произвольные поля», «Платформа», «Дата» — с локализованными заголовками.
    /// </summary>
    public static IReadOnlyList<ScriptTokenOption> BuildOptions(Infobase? infobase)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ScriptTokenOption>();

        var launchContext = Services.ScriptLaunchContextResolver.Resolve(infobase);
        var map = ScriptParameterResolver.BuildValueMap(infobase, ResolveEffectiveCredentials(infobase), launchContext);

        // Порядок секций: статический справочник сначала (по секциям, внутри — по токену),
        // затем оставшиеся динамические ключи карты (тоже по секциям).
        string? lastSection = null;
        void AddOption(string token, string value, string section, string description)
        {
            if (!seen.Add(token))
                return;
            var isStart = section is not null && !string.Equals(section, lastSection, StringComparison.Ordinal);
            result.Add(new ScriptTokenOption(token, value, section, isStart, description));
            if (section is not null)
                lastSection = section;
        }

        foreach (var hint in ScriptScenarioEditViewModel.AvailableTokens
                     .OrderBy(t => SectionOrder(SectionForToken(t.Token)))
                     .ThenBy(t => t.Token, StringComparer.OrdinalIgnoreCase))
        {
            var value = map.TryGetValue(hint.Token.Trim('%'), out var v) ? (v ?? string.Empty) : string.Empty;
            AddOption(hint.Token, value, SectionForToken(hint.Token), LocalizationManager.T(hint.LocalizationKey));
        }

        // Динамические ключи карты (рефлексия по всем свойствам базы): ключи, которых нет
        // в статическом справочнике, тоже доступны — «список полей структуры базы 1-в-1».
        foreach (var key in map.Keys
                     .Where(k => !seen.Contains("%" + k + "%"))
                     .OrderBy(SectionOrderForKey)
                     .ThenBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            var value = map[key] ?? string.Empty;
            var descriptionKey = DescriptionKeyFor(key);
            var description = descriptionKey is null
                ? string.Empty
                : LocalizationManager.T(descriptionKey);
            AddOption("%" + key + "%", value, SectionForToken(key), description);
        }

        // Стабильная сортировка по секциям — секции строго подряд (внутри секции сначала
        // статические токены справочника, затем динамические), чтобы заголовки секций
        // в диалоге не дублировались на стыке «статический → динамический» (issue #67).
        var ordered = result.OrderBy(o => SectionOrder(o.SectionKey ?? string.Empty)).ToList();
        result.Clear();
        string? last = null;
        foreach (var option in ordered)
        {
            var section = option.SectionKey;
            var isStart = section is not null && !string.Equals(section, last, StringComparison.Ordinal);
            result.Add(new ScriptTokenOption(option.Token, option.Value, section, isStart, option.Description));
            if (section is not null)
                last = section;
        }

        return result;
    }

    /// <summary>Секция по ключу подстановки (регистронезависимо).</summary>
    private static string SectionForToken(string token)
    {
        var key = (token ?? string.Empty).Trim('%').ToLowerInvariant();

        if (key.Contains("connection.")
            || key.Equals("строкаподключения", StringComparison.Ordinal)
            || key.Equals("пользователь", StringComparison.Ordinal)
            || key.Equals("пароль", StringComparison.Ordinal)
            || key.Equals("сервер", StringComparison.Ordinal)
            || key.Equals("порт", StringComparison.Ordinal)
            || key.Equals("имянасервере", StringComparison.Ordinal)
            || key.Equals("вебurl", StringComparison.Ordinal)
            || key.Equals("каталог", StringComparison.Ordinal)
            || key.Equals("путьиб", StringComparison.Ordinal))
            return SectionConnection;

        // Раздельная авторизация (issue #67): enterpriseauth.*/configuratorauth.*, русские
        // вложенные «авторизацияпредприятия.…»/«авторизацияконфигуратора.…», плоские
        // enterpriseuser/enterprisepassword/configuratoruser/configuratorpassword и скаляр
        // configuratoruseenterpriseauth (флаг «авторизация как для Предприятия»).
        if (key.Contains("enterpriseauth", StringComparison.Ordinal)
            || key.Contains("configuratorauth", StringComparison.Ordinal)
            || key.Contains("авторизацияпредприятия", StringComparison.Ordinal)
            || key.Contains("авторизацияконфигуратора", StringComparison.Ordinal)
            || key.Equals("enterpriseuser", StringComparison.Ordinal)
            || key.Equals("enterprisepassword", StringComparison.Ordinal)
            || key.Equals("configuratoruser", StringComparison.Ordinal)
            || key.Equals("configuratorpassword", StringComparison.Ordinal))
            return SectionAuth;

        if (key.Equals("date", StringComparison.Ordinal)
            || key.StartsWith("date:", StringComparison.Ordinal)
            || key.Equals("дата", StringComparison.Ordinal)
            || key.StartsWith("дата:", StringComparison.Ordinal))
            return SectionDate;

        if (key.Contains("full_launch", StringComparison.Ordinal)
            || key.Contains("full_exe", StringComparison.Ordinal)
            || key.Contains("platform_version", StringComparison.Ordinal)
            || key.Contains("полнаястрока", StringComparison.Ordinal)
            || key.Contains("путьплатформы", StringComparison.Ordinal)
            || key.Contains("версияплатформы", StringComparison.Ordinal))
            return SectionPlatform;

        if (key.Contains("additional", StringComparison.Ordinal)
            || key.Contains("дополнительн", StringComparison.Ordinal)
            || key.Contains("extrafields", StringComparison.Ordinal))
            return SectionExtra;

        return SectionMain;
    }

    private static int SectionOrderForKey(string key) => SectionOrder(SectionForToken(key));

    private static int SectionOrder(string section) => section switch
    {
        SectionMain => 0,
        SectionConnection => 1,
        SectionAuth => 2,
        SectionExtra => 3,
        SectionPlatform => 4,
        SectionDate => 5,
        _ => 6
    };

    /// <summary>
    /// Каталог описаний для динамических ключей карты (issue #67): для известных ключей —
    /// ключ локализации человекочитаемого пояснения («что означает значение и откуда
    /// берётся»), для остальных — обобщённая подпись по типу ключа (или <c>null</c> —
    /// универсальная <c>Script.TokenDescriptionFallback</c>).
    /// </summary>
    private static string? DescriptionKeyFor(string token)
    {
        var key = (token ?? string.Empty).Trim('%').ToLowerInvariant();
        switch (key)
        {
            case "connection.user":
            case "пользователь":
                return "Script.TokenConnectionUser";

            case "connection.password":
            case "пароль":
            case "password":
                return "Script.TokenConnectionPassword";

            case "enterpriseauth.user":
            case "enterpriseuser":
            case "авторизацияпредприятия.логин":
                return "Script.TokenEnterpriseAuthUser";

            case "enterpriseauth.password":
            case "enterprisepassword":
            case "авторизацияпредприятия.пароль":
                return "Script.TokenEnterpriseAuthPassword";

            case "enterpriseauth.mode":
            case "авторизацияпредприятия.режим":
                return "Script.TokenEnterpriseAuthMode";

            case "configuratorauth.user":
            case "configuratoruser":
            case "авторизацияконфигуратора.логин":
                return "Script.TokenConfiguratorAuthUser";

            case "configuratorauth.password":
            case "configuratorpassword":
            case "авторизацияконфигуратора.пароль":
                return "Script.TokenConfiguratorAuthPassword";

            case "configuratorauth.mode":
            case "авторизацияконфигуратора.режим":
                return "Script.TokenConfiguratorAuthMode";

            case "configuratoruseenterpriseauth":
                return "Script.TokenConfiguratorUseEnterpriseAuth";

            case "name":
            case "имябазы":
                return "Script.TokenName";

            case "id":
                return "Script.TokenBaseId";

            case "имягруппы":
            case "группа":
                return "Script.TokenDescrGroup";

            case "тэги":
            case "tags":
                return "Script.TokenDescrTags";

            case "дополнительныеполя":
            case "extrafields":
                return "Script.TokenDescrExtraFields";

            case "date":
            case "дата":
                return "Script.TokenDescrDate";

            case "full_launch_enterprise":
            case "полнаястрокапредприятие":
                return "Script.TokenFullLaunchEnterprise";

            case "full_launch_config":
            case "полнаястрокаконфигуратор":
                return "Script.TokenFullLaunchConfig";

            case "full_exe_name":
            case "путьплатформы":
                return "Script.TokenFullExeName";

            case "platform_version":
            case "версияплатформы":
                return "Script.TokenPlatformVersion";
        }

        // Обобщённые подписи по типу ключа: connection.* и произвольные поля extrafields.
        if (key.StartsWith("connection.", StringComparison.Ordinal))
            return "Script.TokenDescrConnectionAny";
        if (key.StartsWith("additional", StringComparison.Ordinal)
            || key.StartsWith("дополнительн", StringComparison.Ordinal))
            return "Script.TokenDescrAdditionalN";

        return null;
    }

    /// <summary>Решённая авторизация режима «1С:Предприятие» (общая авторизация, issue #57).</summary>
    private static (string? User, string? Password)? ResolveEffectiveCredentials(Infobase? infobase)
    {
        if (infobase is null)
            return null;

        InfobaseAuthResolver.Resolve(
            infobase, OneCLaunchMode.Enterprise,
            out var authMode, out var authUser, out var authPassword,
            OneCLauncher.CommonEnterpriseAuthorization,
            OneCLauncher.CommonConfiguratorAuthorization,
            OneCLauncher.CommonAuthorization);
        return authMode == AuthenticationMode.Credentials
            ? (authUser, authPassword)
            : null;
    }
}