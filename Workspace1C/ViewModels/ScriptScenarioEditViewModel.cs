using System.Collections.ObjectModel;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// Подсказка-токен подстановки: сам токен (<c>%name%</c>), ключ локализации
/// краткого описания значения и готовое описание для отображения в списке
/// (issue #308: двойной клик вставляет ТОЛЬКО токен).
/// </summary>
public sealed class ScriptTokenHint
{
    public ScriptTokenHint(string token, string localizationKey, string? description = null)
    {
        Token = token;
        LocalizationKey = localizationKey;
        Description = description;
    }

    /// <summary>Токен подстановки, например <c>%name%</c>.</summary>
    public string Token { get; }

    /// <summary>Ключ локализации описания токена.</summary>
    public string LocalizationKey { get; }

    /// <summary>Локализованное описание токена (для списка), может быть <c>null</c>.</summary>
    public string? Description { get; }
}

/// <summary>
/// Форма редактирования сценария запуска скрипта: наименование, путь к файлу
/// и параметры (строки). Чистый .NET, используется обеими платформами.
/// </summary>
public class ScriptScenarioEditViewModel : ViewModelBase
{
    /// <summary>Доступные токены подстановки (вставляются двойным кликом в поле параметров).</summary>
    public static readonly IReadOnlyList<ScriptTokenHint> AvailableTokens = new[]
    {
        new ScriptTokenHint("%name%", "Script.TokenName"),
        new ScriptTokenHint("%connection.server%", "Script.TokenConnectionServer"),
        new ScriptTokenHint("%connection.serverPort%", "Script.TokenConnectionServerPort"),
        new ScriptTokenHint("%connection.database%", "Script.TokenConnectionDatabase"),
        new ScriptTokenHint("%connection.filePath%", "Script.TokenConnectionFilePath"),
        new ScriptTokenHint("%connection.webUrl%", "Script.TokenConnectionWebUrl"),
        new ScriptTokenHint("%connection.connectionString%", "Script.TokenConnectionString"),
        // Логин и остальные свойства запуска (issue #67): резолвер понимает ВСЕ
        // реквизиты базы динамически (%connection.<имя>%; решённый логин при общей
        // авторизации issue #57 подставляется, когда у базы нет собственных данных).
        new ScriptTokenHint("%connection.user%", "Script.TokenConnectionUser"),
        new ScriptTokenHint("%connection.password%", "Script.TokenConnectionPassword"),
        new ScriptTokenHint("%connection.type%", "Script.TokenConnectionType"),
        new ScriptTokenHint("%connection.port%", "Script.TokenConnectionPort"),
        new ScriptTokenHint("%password%", "Script.TokenPassword"),
        new ScriptTokenHint("%id%", "Script.TokenBaseId"),
        new ScriptTokenHint("%имягруппы%", "Script.TokenGroupName"),
        new ScriptTokenHint("%date%", "Script.TokenDate"),
        new ScriptTokenHint("%date:yyyyMMdd_HHmm%", "Script.TokenDateCustom"),
        // Полные строки запуска и платформа (issue #67): токены из первого сообщения
        // issue — «полная строка запуска, которую бы стартер подставил для текущей
        // базы» (Предприятие/Конфигуратор), путь к файлу платформы и её версия.
        new ScriptTokenHint("%full_launch_enterprise%", "Script.TokenFullLaunchEnterprise"),
        new ScriptTokenHint("%full_launch_config%", "Script.TokenFullLaunchConfig"),
        new ScriptTokenHint("%full_exe_name%", "Script.TokenFullExeName"),
        new ScriptTokenHint("%platform_version%", "Script.TokenPlatformVersion"),
        // Произвольные поля базы (issue #6): пример подстановки; номер может быть
        // любым (1..N по строкам таблицы «Произвольные поля» вкладки «Идентификатор»).
        new ScriptTokenHint("%Additional_1%", "Script.TokenAdditional")
    };

    public ScriptScenarioEditViewModel(ScriptScenario? scenario)
    {
        if (scenario is not null)
        {
            Name = scenario.Name;
            FilePath = scenario.FilePath ?? "";
            WorkingDirectory = scenario.WorkingDirectory ?? "";
            HideWindow = scenario.HideWindow;
            NotifyOnStart = scenario.NotifyOnStart;
            KeepOpen = scenario.KeepOpen;
            Shell = scenario.Shell;
            foreach (var parameter in scenario.Parameters ?? new List<string>())
            {
                if (!string.IsNullOrWhiteSpace(parameter))
                    Parameters.Add(parameter.Trim());
            }
        }
        else
        {
            Parameters.Add("");
        }

        ParametersText = string.Join("\n", Parameters);
    }

    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";

    /// <summary>
    /// Папка запуска сценария (рабочий каталог процесса). Пусто — процесс наследует
    /// рабочий каталог приложения (issue #308, п.7).
    /// </summary>
    public string WorkingDirectory { get; set; } = "";

    /// <summary>
    /// Скрывать окно запущенного скрипта (issue #308): <c>true</c> — окно скрыто
    /// (по умолчанию), <c>false</c> — консольное окно видимо.
    /// </summary>
    public bool HideWindow { get; set; } = true;

    /// <summary>
    /// Уведомлять о запуске сценария (issue #308): <c>true</c> — после запуска
    /// показывается диалог «скрипт запущен» и системное уведомление; <c>false</c>
    /// (по умолчанию) — запуск без диалога и уведомлений.
    /// </summary>
    public bool NotifyOnStart { get; set; }

    /// <summary>
    /// Не закрывать окно после завершения сценария (issue #308): <c>true</c> —
    /// к команде добавляется хвост удержания окна (pause / Read-Host / read).
    /// </summary>
    public bool KeepOpen { get; set; }

    /// <summary>
    /// Интерпретатор (shell) для запуска сценария (issue #308, п.9):
    /// Авто / cmd / PowerShell / sh. Дефолт — <see cref="ScriptShell.Auto"/>
    /// (по платформе, как раньше).
    /// </summary>
    public ScriptShell Shell { get; set; } = ScriptShell.Auto;

    /// <summary>
    /// Доступные значения интерпретатора для выпадающего списка формы
    /// (порядок: Авто, cmd, PowerShell, sh).
    /// </summary>
    public static IReadOnlyList<ScriptShell> ShellOptions { get; } =
        new[] { ScriptShell.Auto, ScriptShell.Cmd, ScriptShell.PowerShell, ScriptShell.Sh };

    /// <summary>Параметры одной строкой через переводы строк (каждая строка — параметр).</summary>
    public string ParametersText { get; set; } = "";

    public ObservableCollection<string> Parameters { get; } = new();

    /// <summary>Разобранные (непустые, обрезанные) параметры формы.</summary>
    public List<string> NonEmptyParameters =>
        (ParametersText ?? "")
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n')
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();

    /// <summary>
    /// Валидация полей формы. Возвращает ключ локализации ошибки либо <c>null</c>,
    /// если всё корректно (имя и путь к файлу непустые).
    /// </summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            return "Script.NameRequired";
        if (string.IsNullOrWhiteSpace(FilePath))
            return "Script.FilePathRequired";
        return null;
    }

    /// <summary>
    /// Переносит заполненные поля формы в сценарий. Идентификатор <see cref="ScriptScenario.Id"/>
    /// НЕ трогается (issue #308: редактирование не плодит копию — Store.Save по тому же Id
    /// перезаписывает тот же файл).
    /// </summary>
    public void ApplyTo(ScriptScenario scenario)
    {
        scenario.Name = Name.Trim();
        scenario.FilePath = FilePath.Trim();
        scenario.WorkingDirectory = WorkingDirectory.Trim();
        scenario.Parameters = NonEmptyParameters;
        scenario.HideWindow = HideWindow;
        scenario.NotifyOnStart = NotifyOnStart;
        scenario.KeepOpen = KeepOpen;
        scenario.Shell = Shell;
    }

    /// <summary>
    /// Пример полной командной строки сценария с подстановками для указанной базы
    /// (используется как живая подсказка в окне редактирования и выбора). Включает
    /// обёртку выбранного интерпретатора (issue #308, п.9): превью отражает шелл
    /// сценария, включая разделитель «cd …» — «;» для PowerShell, «&&» для
    /// cmd/sh (issue #308, замечание @7OH).
    /// </summary>
    public static string BuildExampleCommandLine(ScriptScenario scenario, Infobase? infobase, DateTime? now = null)
    {
        // Решённая авторизация (логин при общей авторизации, issue #57) — превью
        // соответствует фактическому запуску; контекст платформы (полные строки запуска,
        // exe, версия) — токены %full_launch_*%, %full_exe_name%, %platform_version% (issue #67).
        var values = ScriptParameterResolver.BuildValueMap(
            infobase, ResolvePreviewCredentials(infobase), Services.ScriptLaunchContextResolver.Resolve(infobase));
        // Сборка использует scenario.Shell: разделитель и обёртка соответствуют
        // выбранному интерпретатору; платформа — текущая (для режима «Авто»).
        return ScriptParameterResolver.BuildShellCommandLine(scenario, values, now, OperatingSystem.IsWindows());
    }

    /// <summary>
    /// Логин/пароль, решённые для режима «1С:Предприятие» (общая авторизация
    /// issue #57), для превью подстановок и диалога выбора переменной (issue #67).
    /// null — аутентификация не «Логин+Пароль» (ОС или запрос платформы).
    /// </summary>
    private static (string? User, string? Password)? ResolvePreviewCredentials(Infobase? infobase)
    {
        if (infobase is null)
            return null;

        Services.InfobaseAuthResolver.Resolve(
            infobase, Services.OneCLaunchMode.Enterprise,
            out var authMode, out var authUser, out var authPassword,
            Services.OneCLauncher.CommonEnterpriseAuthorization,
            Services.OneCLauncher.CommonConfiguratorAuthorization,
            Services.OneCLauncher.CommonAuthorization);
        return authMode == Models.AuthenticationMode.Credentials
            ? (authUser, authPassword)
            : null;
    }
}