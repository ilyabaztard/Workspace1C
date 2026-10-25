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
/// переменной (токен %…%) и значение для выбранной базы (для превью; пароль маскируется).
/// </summary>
public sealed class ScriptTokenOption
{
    public ScriptTokenOption(string token, string value)
    {
        Token = token;
        Value = value;
    }

    /// <summary>Итоговое имя переменной (токен вида <c>%connection.server%</c>).</summary>
    public string Token { get; }

    /// <summary>Значение для выбранной базы (может быть пустым).</summary>
    public string Value { get; }

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
/// ViewModel окна «Выбор переменной» для подстановок сценариев (issue #67): дерево/
/// список всех переменных базы («итоговое имя переменной — значение для базы»),
/// поиск по обеим колонкам, выбор по «Ок» и двойному клику. Чистый .NET — обе платформы.
/// </summary>
public sealed class ScriptTokenPickerViewModel : ViewModelBase
{
    private readonly IReadOnlyList<ScriptTokenOption> _all;
    private string _query = string.Empty;
    private ScriptTokenOption? _selectedItem;

    public ScriptTokenPickerViewModel(Infobase? infobase)
    {
        _all = BuildOptions(infobase);
        Items = new ObservableCollection<ScriptTokenOption>(_all);
        SelectedItem = Items.FirstOrDefault();
    }

    /// <summary>Отфильтрованный список переменных.</summary>
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

    /// <summary>Фильтр строк по запросу (регистронезависимо, имя или значение).</summary>
    public static IReadOnlyList<ScriptTokenOption> FilterOptions(
        IReadOnlyList<ScriptTokenOption> options, string? query)
    {
        var q = (query ?? string.Empty).Trim();
        if (q.Length == 0)
            return options;
        return options
            .Where(o => o.Token.Contains(q, StringComparison.OrdinalIgnoreCase)
                || o.Value.Contains(q, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private void ApplyFilter()
    {
        Items.Clear();
        foreach (var option in FilterOptions(_all, Query))
            Items.Add(option);
        SelectedItem = Items.FirstOrDefault();
    }

    /// <summary>
    /// Строит полный список переменных сценария для базы: статический справочник
    /// окна (описания) + все динамические ключи карты <see cref="ScriptParameterResolver.BuildValueMap"/>
    /// (рефлексия «понимает все реквизиты базы», issue #67); решённая авторизация
    /// (общая, issue #57) учитывается для логина/пароля.
    /// </summary>
    public static IReadOnlyList<ScriptTokenOption> BuildOptions(Infobase? infobase)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ScriptTokenOption>();

        // Статические токены справочника окна — с человекочитаемыми описаниями.
        foreach (var hint in ScriptScenarioEditViewModel.AvailableTokens)
        {
            if (!seen.Add(hint.Token))
                continue;
            var value = ValueForToken(hint.Token, infobase);
            result.Add(new ScriptTokenOption(hint.Token, value));
        }

        // Динамические ключи карты подстановок (рефлексия по всем свойствам базы):
        // ключи, которых нет в статическом справочнике, тоже доступны.
        var map = ScriptParameterResolver.BuildValueMap(infobase, ResolveEffectiveCredentials(infobase));
        foreach (var pair in map.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            var token = "%" + pair.Key + "%";
            if (!seen.Add(token))
                continue;
            result.Add(new ScriptTokenOption(token, pair.Value ?? string.Empty));
        }

        return result;
    }

    private static string ValueForToken(string token, Infobase? infobase)
    {
        var map = ScriptParameterResolver.BuildValueMap(infobase, ResolveEffectiveCredentials(infobase));
        var key = token.Trim('%');
        return map.TryGetValue(key, out var value) ? (value ?? string.Empty) : string.Empty;
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