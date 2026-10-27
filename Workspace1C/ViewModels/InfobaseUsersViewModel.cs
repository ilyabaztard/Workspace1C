using System.Collections.ObjectModel;
using System.Text;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>Вид пакетной операции над пользователем в отмеченных базах.</summary>
public enum InfobaseUserBatchOperation
{
    /// <summary>Создать пользователя или изменить существующего.</summary>
    Upsert,

    /// <summary>Сменить пароль.</summary>
    SetPassword,

    /// <summary>Отключить вход (снять аутентификацию 1С и ОС, скрыть из списка выбора).</summary>
    DisableLogin,

    /// <summary>Удалить пользователя.</summary>
    Delete
}

/// <summary>
/// ViewModel окна «Пользователи информационных баз» (выпуск 1.0.0.83): список
/// пользователей выбранной базы и пакетные операции над одним пользователем во всех
/// отмеченных базах (создать/изменить, сменить пароль, отключить вход, удалить).
/// Чистый .NET — обе платформы; сами операции выполняются через COM (Windows).
/// </summary>
public sealed class InfobaseUsersViewModel : ViewModelBase
{
    private readonly List<ExtensionTargetRow> _allTargets;
    private readonly StringBuilder _log = new();
    private CancellationTokenSource? _cts;
    private string _searchText = string.Empty;
    private ExtensionTargetRow? _selectedTarget;
    private InfobaseUserRow? _selectedUser;
    private bool _isBusy;
    private string _userName = string.Empty;
    private string _fullName = string.Empty;
    private string _password = string.Empty;
    private bool _setPassword = true;
    private bool _standardAuthentication = true;
    private bool _osAuthentication;
    private string _osUser = string.Empty;
    private bool _showInList = true;
    private string _rolesText = string.Empty;
    private bool _replaceRoles;
    private string _availableRolesText = string.Empty;

    public InfobaseUsersViewModel(IEnumerable<Infobase> infobases, Infobase? selected)
    {
        _allTargets = (infobases ?? Array.Empty<Infobase>())
            .Where(ib => ib is not null && ib.Connection is not null
                         && ib.Connection.Type is ConnectionType.File or ConnectionType.ClientServer)
            .OrderBy(ib => ib.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(ib => new ExtensionTargetRow(ib))
            .ToList();
        foreach (var row in _allTargets)
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ExtensionTargetRow.IsChecked))
                    OnPropertyChanged(nameof(CheckedSummary));
            };

        if (selected is not null)
        {
            var row = _allTargets.FirstOrDefault(r => ReferenceEquals(r.Infobase, selected))
                      ?? _allTargets.FirstOrDefault(r => r.Infobase.Id == selected.Id);
            if (row is not null)
            {
                row.IsChecked = true;
                _selectedTarget = row;
            }
        }
        ApplyFilter();
    }

    /// <summary>Операции через COM доступны только в Windows.</summary>
    public bool IsSupported => OperatingSystem.IsWindows();

    public string PlatformHint => IsSupported
        ? LocalizationManager.T("IbUsers.Hint")
        : LocalizationManager.T("ComScript.WindowsOnly");

    public ObservableCollection<ExtensionTargetRow> Targets { get; } = new();

    public ObservableCollection<InfobaseUserRow> Users { get; } = new();

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
                ApplyFilter();
        }
    }

    public ExtensionTargetRow? SelectedTarget
    {
        get => _selectedTarget;
        set
        {
            if (SetProperty(ref _selectedTarget, value))
            {
                Users.Clear();
                SelectedUser = null;
                AvailableRolesText = string.Empty;
                OnPropertyChanged(nameof(SelectedBaseText));
            }
        }
    }

    public string SelectedBaseText => _selectedTarget is null
        ? LocalizationManager.T("IbUsers.NoBaseSelected")
        : string.Format(LocalizationManager.T("IbUsers.SelectedBase"), _selectedTarget.Name);

    public InfobaseUserRow? SelectedUser
    {
        get => _selectedUser;
        set
        {
            if (SetProperty(ref _selectedUser, value) && value is not null)
                FillFormFrom(value.User);
        }
    }

    public string UserName { get => _userName; set => SetProperty(ref _userName, value ?? string.Empty); }

    public string FullName { get => _fullName; set => SetProperty(ref _fullName, value ?? string.Empty); }

    public string Password { get => _password; set => SetProperty(ref _password, value ?? string.Empty); }

    public bool SetPasswordFlag { get => _setPassword; set => SetProperty(ref _setPassword, value); }

    public bool StandardAuthentication { get => _standardAuthentication; set => SetProperty(ref _standardAuthentication, value); }

    public bool OSAuthentication { get => _osAuthentication; set => SetProperty(ref _osAuthentication, value); }

    public string OSUser { get => _osUser; set => SetProperty(ref _osUser, value ?? string.Empty); }

    public bool ShowInList { get => _showInList; set => SetProperty(ref _showInList, value); }

    public string RolesText { get => _rolesText; set => SetProperty(ref _rolesText, value ?? string.Empty); }

    public bool ReplaceRoles { get => _replaceRoles; set => SetProperty(ref _replaceRoles, value); }

    /// <summary>Роли конфигурации выбранной базы (для подсказки при вводе).</summary>
    public string AvailableRolesText
    {
        get => _availableRolesText;
        private set => SetProperty(ref _availableRolesText, value ?? string.Empty);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
                OnPropertyChanged(nameof(IsIdle));
        }
    }

    public bool IsIdle => !_isBusy;

    public string LogText => _log.ToString();

    public string CheckedSummary => string.Format(LocalizationManager.T("IbUsers.CheckedSummary"),
        _allTargets.Count(r => r.IsChecked), _allTargets.Count);

    public IReadOnlyList<ExtensionTargetRow> CheckedTargets => _allTargets.Where(r => r.IsChecked).ToList();

    public void SetAllChecked(bool value)
    {
        foreach (var row in Targets)
            row.IsChecked = value;
    }

    public void Cancel()
    {
        try { _cts?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    public void AppendLog(string message)
    {
        _log.Append(DateTime.Now.ToString("HH:mm:ss")).Append("  ").AppendLine(message);
        OnPropertyChanged(nameof(LogText));
    }

    /// <summary>Очищает форму для ввода нового пользователя.</summary>
    public void NewUser()
    {
        SelectedUser = null;
        UserName = string.Empty;
        FullName = string.Empty;
        Password = string.Empty;
        SetPasswordFlag = true;
        StandardAuthentication = true;
        OSAuthentication = false;
        OSUser = string.Empty;
        ShowInList = true;
        RolesText = string.Empty;
        ReplaceRoles = false;
    }

    /// <summary>Параметры пользователя из формы.</summary>
    public InfobaseUserSpec BuildSpec() => new()
    {
        Name = UserName.Trim(),
        FullName = FullName.Trim(),
        SetPassword = SetPasswordFlag,
        Password = Password,
        StandardAuthentication = StandardAuthentication,
        OSAuthentication = OSAuthentication,
        OSUser = OSUser.Trim(),
        ShowInList = ShowInList,
        Roles = InfobaseUsersService.ParseRoles(RolesText),
        ReplaceRoles = ReplaceRoles
    };

    private void FillFormFrom(InfobaseUserInfo user)
    {
        UserName = user.Name;
        FullName = user.FullName;
        Password = string.Empty;
        SetPasswordFlag = false;
        StandardAuthentication = user.StandardAuthentication;
        OSAuthentication = user.OSAuthentication;
        OSUser = user.OSUser;
        ShowInList = user.ShowInList;
    }

    private void ApplyFilter()
    {
        Targets.Clear();
        var needle = _searchText.Trim();
        foreach (var row in _allTargets)
        {
            if (needle.Length == 0 || row.Name.IndexOf(needle, StringComparison.CurrentCultureIgnoreCase) >= 0)
                Targets.Add(row);
        }
    }

    private CancellationToken BeginOperation()
    {
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        IsBusy = true;
        return _cts.Token;
    }

    /// <summary>Читает пользователей и роли выбранной базы.</summary>
    public async Task RefreshUsersAsync()
    {
        if (IsBusy || _selectedTarget is null)
            return;
        var row = _selectedTarget;
        var token = BeginOperation();
        try
        {
            AppendLog(string.Format(LocalizationManager.T("IbUsers.LogListing"), row.Name));
            var result = await InfobaseUsersService.ListAsync(row.Infobase, token);
            Users.Clear();
            if (!result.Success)
            {
                AppendLog($"{row.Name}: {result.Error}");
                return;
            }
            foreach (var user in result.Users)
                Users.Add(new InfobaseUserRow(user));
            AvailableRolesText = string.Join(", ", result.Roles);
            AppendLog(string.Format(LocalizationManager.T("IbUsers.LogListed"), row.Name, result.Users.Count, result.Roles.Count));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Подставляет в поле ролей роли выбранного пользователя выбранной базы.</summary>
    public async Task LoadSelectedUserRolesAsync()
    {
        if (IsBusy || _selectedTarget is null || _selectedUser is null)
            return;
        var row = _selectedTarget;
        var name = _selectedUser.Name;
        var token = BeginOperation();
        try
        {
            var result = await InfobaseUsersService.GetUserRolesAsync(row.Infobase, name, token);
            if (!result.Success)
            {
                AppendLog($"{row.Name}: {result.Error}");
                return;
            }
            if (!result.Found)
            {
                AppendLog(string.Format(LocalizationManager.T("IbUsers.LogNotFound"), name, row.Name));
                return;
            }
            RolesText = string.Join(", ", result.Roles);
            AppendLog(string.Format(LocalizationManager.T("IbUsers.LogRoles"), name, result.Roles.Count));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Текст ошибки проверки формы для операции или null.</summary>
    public string? ValidateFor(InfobaseUserBatchOperation operation)
    {
        if (string.IsNullOrWhiteSpace(UserName))
            return LocalizationManager.T("IbUsers.NameRequired");
        return operation == InfobaseUserBatchOperation.Upsert ? InfobaseUsersService.Validate(BuildSpec()) : null;
    }

    /// <summary>
    /// Выполняет операцию над пользователем <see cref="UserName"/> во всех отмеченных базах
    /// по очереди. Возвращает число успешных баз.
    /// </summary>
    public async Task<int> RunBatchAsync(InfobaseUserBatchOperation operation)
    {
        if (IsBusy)
            return 0;
        var targets = CheckedTargets;
        if (targets.Count == 0)
        {
            AppendLog(LocalizationManager.T("IbUsers.NoChecked"));
            return 0;
        }
        var validation = ValidateFor(operation);
        if (validation is not null)
        {
            AppendLog(validation);
            return 0;
        }

        var spec = BuildSpec();
        var token = BeginOperation();
        var ok = 0;
        try
        {
            foreach (var row in targets)
                row.Status = LocalizationManager.T("Extensions.StatusQueued");

            foreach (var row in targets)
            {
                if (token.IsCancellationRequested)
                {
                    row.Status = LocalizationManager.T("Extensions.StatusCancelled");
                    continue;
                }
                row.Status = LocalizationManager.T("Extensions.StatusRunning");
                var result = operation switch
                {
                    InfobaseUserBatchOperation.Upsert => await InfobaseUsersService.UpsertAsync(row.Infobase, spec, token),
                    InfobaseUserBatchOperation.SetPassword => await InfobaseUsersService.SetPasswordAsync(row.Infobase, spec.Name, spec.Password, token),
                    InfobaseUserBatchOperation.DisableLogin => await InfobaseUsersService.DisableLoginAsync(row.Infobase, spec.Name, token),
                    _ => await InfobaseUsersService.DeleteAsync(row.Infobase, spec.Name, token)
                };

                if (!result.Success)
                {
                    row.Status = LocalizationManager.T("Extensions.StatusError");
                    AppendLog($"{row.Name}: {result.Error}");
                    continue;
                }
                if (!result.Found)
                {
                    row.Status = LocalizationManager.T("IbUsers.StatusNotFound");
                    AppendLog(string.Format(LocalizationManager.T("IbUsers.LogNotFound"), spec.Name, row.Name));
                    continue;
                }

                ok++;
                row.Status = LocalizationManager.T("Extensions.StatusDone");
                AppendLog(DescribeSuccess(operation, spec.Name, row.Name, result));
            }
            AppendLog(string.Format(LocalizationManager.T("IbUsers.LogSummary"), ok, targets.Count));
        }
        finally
        {
            IsBusy = false;
        }

        if (ok > 0 && _selectedTarget is not null && targets.Contains(_selectedTarget))
            await RefreshUsersAsync();
        return ok;
    }

    private static string DescribeSuccess(InfobaseUserBatchOperation operation, string user, string baseName,
        InfobaseUsersResult result)
    {
        var text = operation switch
        {
            InfobaseUserBatchOperation.Upsert => string.Format(LocalizationManager.T(result.Created
                ? "IbUsers.LogCreated" : "IbUsers.LogUpdated"), user, baseName),
            InfobaseUserBatchOperation.SetPassword => string.Format(LocalizationManager.T("IbUsers.LogPassword"), user, baseName),
            InfobaseUserBatchOperation.DisableLogin => string.Format(LocalizationManager.T("IbUsers.LogDisabled"), user, baseName),
            _ => string.Format(LocalizationManager.T("IbUsers.LogDeleted"), user, baseName)
        };
        if (result.MissingRoles.Count > 0)
            text += " " + string.Format(LocalizationManager.T("IbUsers.LogMissingRoles"), string.Join(", ", result.MissingRoles));
        return text;
    }
}

/// <summary>Строка таблицы пользователей базы.</summary>
public sealed class InfobaseUserRow
{
    public InfobaseUserRow(InfobaseUserInfo user)
    {
        User = user ?? throw new ArgumentNullException(nameof(user));
    }

    public InfobaseUserInfo User { get; }

    public string Name => User.Name;

    public string FullName => User.FullName;

    /// <summary>Способы входа: «1С», «ОС», «1С + ОС» или «вход отключён».</summary>
    public string AuthText
    {
        get
        {
            if (User.IsLoginDisabled)
                return LocalizationManager.T("IbUsers.AuthDisabled");
            if (User.StandardAuthentication && User.OSAuthentication)
                return LocalizationManager.T("IbUsers.AuthBoth");
            return User.StandardAuthentication
                ? LocalizationManager.T("IbUsers.AuthStandard")
                : LocalizationManager.T("IbUsers.AuthOs");
        }
    }

    public string PasswordText => User.PasswordIsSet
        ? LocalizationManager.T("IbUsers.PasswordSet")
        : LocalizationManager.T("IbUsers.PasswordEmpty");

    public string OSUser => User.OSUser;
}

/// <summary>Тексты подтверждений пакетных операций окна «Пользователи ИБ» (обе платформы).</summary>
public static class InfobaseUsersDialogs
{
    public static string ConfirmText(InfobaseUserBatchOperation operation, string userName, int count)
    {
        var key = operation switch
        {
            InfobaseUserBatchOperation.Upsert => "IbUsers.ConfirmUpsert",
            InfobaseUserBatchOperation.SetPassword => "IbUsers.ConfirmPassword",
            InfobaseUserBatchOperation.DisableLogin => "IbUsers.ConfirmDisable",
            _ => "IbUsers.ConfirmDelete"
        };
        return string.Format(LocalizationManager.T(key), userName?.Trim() ?? string.Empty, count);
    }
}
