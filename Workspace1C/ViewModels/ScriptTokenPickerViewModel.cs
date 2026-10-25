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
/// секция («Основные»/«Подключение»/…) и признак начала секции для заголовков в списке.
/// </summary>
public sealed class ScriptTokenOption
{
    public ScriptTokenOption(string token, string value, string? sectionKey = null, bool sectionStart = false)
    {
        Token = token;
        Value = value;
        SectionKey = sectionKey;
        SectionStart = sectionStart;
    }

    /// <summary>Итоговое имя переменной (токен вида <c>%connection.server%</c>).</summary>
    public string Token { get; }

    /// <summary>Значение для выбранной базы (может быть пустым).</summary>
    public string Value { get; }

    /// <summary>Ключ локализации секции («Основные», «Подключение», …); null — без секции.</summary>
    public string? SectionKey { get; }

    /// <summary>Признак первой строки секции — перед ней показывается заголовок.</summary>
    public bool SectionStart { get; }

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
    /// Фильтр строк по запросу (регистронезависимо, имя или значение). Порядок исходного
    /// списка сохраняется; флаги начала секций пересчитываются, чтобы после фильтрации
    /// заголовки секций оставались корректными (первая строка каждой секции — с флагом).
    /// </summary>
    public static IReadOnlyList<ScriptTokenOption> FilterOptions(
        IReadOnlyList<ScriptTokenOption> options, string? query)
    {
        var q = (query ?? string.Empty).Trim();
        IEnumerable<ScriptTokenOption> source = options;
        if (q.Length > 0)
            source = options.Where(o => o.Token.Contains(q, StringComparison.OrdinalIgnoreCase)
                || o.Value.Contains(q, StringComparison.OrdinalIgnoreCase));

        var result = new List<ScriptTokenOption>();
        string? currentSection = null;
        foreach (var option in source)
        {
            var section = option.SectionKey;
            var isStart = section is not null && !string.Equals(section, currentSection, StringComparison.Ordinal);
            result.Add(new ScriptTokenOption(option.Token, option.Value, section, isStart));
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
    /// авторизация (общая, issue #57) учитывается для логина/пароля; контекст платформы
    /// (<c>%full_launch_*%</c>, <c>%full_exe_name%</c>, <c>%platform_version%</c>) добавляется
    /// в секцию «Платформа». Итоговый список упорядочен по секциям: «Основные», «Подключение»,
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
        void AddOption(string token, string value, string section)
        {
            if (!seen.Add(token))
                return;
            var isStart = section is not null && !string.Equals(section, lastSection, StringComparison.Ordinal);
            result.Add(new ScriptTokenOption(token, value, section, isStart));
            if (section is not null)
                lastSection = section;
        }

        // Статические токены справочника окна — с человекочитаемыми описаниями.
        foreach (var hint in ScriptScenarioEditViewModel.AvailableTokens
                     .OrderBy(t => SectionOrder(SectionForToken(t.Token)))
                     .ThenBy(t => t.Token, StringComparer.OrdinalIgnoreCase))
        {
            var value = map.TryGetValue(hint.Token.Trim('%'), out var v) ? (v ?? string.Empty) : string.Empty;
            AddOption(hint.Token, value, SectionForToken(hint.Token));
        }

        // Динамические ключи карты (рефлексия по всем свойствам базы): ключи, которых нет
        // в статическом справочнике, тоже доступны — «список полей структуры базы 1-в-1».
        foreach (var key in map.Keys
                     .Where(k => !seen.Contains("%" + k + "%"))
                     .OrderBy(SectionOrderForKey)
                     .ThenBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            AddOption("%" + key + "%", map[key] ?? string.Empty, SectionForToken(key));
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
            result.Add(new ScriptTokenOption(option.Token, option.Value, section, isStart));
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
        SectionExtra => 2,
        SectionPlatform => 3,
        SectionDate => 4,
        _ => 5
    };

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