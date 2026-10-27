namespace Workspace1C.Models;

/// <summary>
/// Пользователь информационной базы 1С (объект <c>ПользовательИнформационнойБазы</c>),
/// прочитанный через COM-коннектор (выпуск 1.0.0.83).
/// </summary>
public sealed class InfobaseUserInfo
{
    /// <summary>Имя (логин).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Полное имя.</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>Аутентификация 1С:Предприятия (по паролю).</summary>
    public bool StandardAuthentication { get; set; }

    /// <summary>Аутентификация операционной системы.</summary>
    public bool OSAuthentication { get; set; }

    /// <summary>Пользователь ОС в формате <c>\\ДОМЕН\Пользователь</c>.</summary>
    public string OSUser { get; set; } = string.Empty;

    /// <summary>Показывать в списке выбора при входе.</summary>
    public bool ShowInList { get; set; }

    /// <summary>Пароль установлен.</summary>
    public bool PasswordIsSet { get; set; }

    /// <summary>Запрещено изменять пароль.</summary>
    public bool CannotChangePassword { get; set; }

    /// <summary>Вход отключён: нет ни аутентификации 1С, ни аутентификации ОС.</summary>
    public bool IsLoginDisabled => !StandardAuthentication && !OSAuthentication;
}

/// <summary>
/// Параметры создания/изменения пользователя ИБ (пакетная операция по отмеченным базам).
/// </summary>
public sealed class InfobaseUserSpec
{
    public string Name { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    /// <summary>Устанавливать пароль (иначе пароль существующего пользователя не меняется).</summary>
    public bool SetPassword { get; set; }

    public string Password { get; set; } = string.Empty;

    public bool StandardAuthentication { get; set; } = true;

    public bool OSAuthentication { get; set; }

    public string OSUser { get; set; } = string.Empty;

    public bool ShowInList { get; set; } = true;

    /// <summary>Имена ролей (как в конфигураторе, например «ПолныеПрава»).</summary>
    public List<string> Roles { get; set; } = new();

    /// <summary>Заменить роли пользователя списком <see cref="Roles"/> (иначе — добавить к имеющимся).</summary>
    public bool ReplaceRoles { get; set; }
}
