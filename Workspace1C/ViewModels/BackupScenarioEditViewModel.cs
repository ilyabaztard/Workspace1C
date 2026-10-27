using System.Collections.ObjectModel;
using Workspace1C.Models;

namespace Workspace1C.ViewModels;

/// <summary>
/// Форма редактирования сценария резервирования: поля формы и валидация.
/// Чистый .NET, используется обеими платформами.
/// </summary>
public class BackupScenarioEditViewModel : ViewModelBase
{
    public BackupScenarioEditViewModel(BackupScenario? scenario)
    {
        if (scenario is not null)
        {
            Name = scenario.Name;
            FileNameTemplate = scenario.FileNameTemplate;
            Format = scenario.Format;
            Engine = scenario.Engine;
            BasePrefix = scenario.BasePrefix ?? "";
            IncludeTimestamp = scenario.IncludeTimestamp;
            KeepCount = Math.Max(0, scenario.KeepCount);
            DeleteOlderThanDays = Math.Max(0, scenario.DeleteOlderThanDays);
            UseInfobaseAuth = scenario.Credential?.UseInfobaseAuth ?? true;
            User = scenario.Credential?.User ?? "";
            Password = scenario.Credential?.Password ?? "";
            foreach (var dir in scenario.TargetDirectories ?? new List<string>())
                TargetDirectories.Add(dir);
        }

        if (TargetDirectories.Count == 0)
            TargetDirectories.Add("");
    }

    public string Name { get; set; } = "";
    public string FileNameTemplate { get; set; } = "{Base}_{Timestamp}";
    public ObservableCollection<string> TargetDirectories { get; } = new();
    public BackupFormat Format { get; set; } = BackupFormat.Dt;

    /// <summary>Движок выгрузки (1.0.0.84).</summary>
    public BackupEngine Engine { get; set; } = BackupEngine.Designer;

    /// <summary>Индекс движка для выпадающего списка (0 — Конфигуратор, 1 — ibcmd).</summary>
    public int EngineIndex
    {
        get => (int)Engine;
        set => Engine = value == 1 ? BackupEngine.Ibcmd : BackupEngine.Designer;
    }

    /// <summary>Подписи движков в порядке <see cref="BackupEngine"/>.</summary>
    public static IReadOnlyList<string> EngineNames => new[]
    {
        Localization.LocalizationManager.T("Backup.EngineDesigner"),
        Localization.LocalizationManager.T("Backup.EngineIbcmd")
    };
    public string BasePrefix { get; set; } = "";
    public bool IncludeTimestamp { get; set; } = true;

    /// <summary>Сколько последних копий хранить (0 — все).</summary>
    public int KeepCount { get; set; }

    /// <summary>Удалять копии старше N дней (0 — не удалять).</summary>
    public int DeleteOlderThanDays { get; set; }

    public bool UseInfobaseAuth { get; set; } = true;
    public string User { get; set; } = "";
    public string Password { get; set; } = "";

    public void AddDirectory() => TargetDirectories.Add("");

    public void RemoveDirectoryAt(int index)
    {
        if (index >= 0 && index < TargetDirectories.Count)
            TargetDirectories.RemoveAt(index);
    }

    /// <summary>
    /// Валидация полей формы. Возвращает ключ локализации ошибки либо <c>null</c>,
    /// если всё корректно (имя непустое, 1–3 каталога назначения).
    /// </summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            return "Backup.NameRequired";

        var dirs = NonEmptyDirectories;
        if (dirs.Count == 0)
            return "Backup.NoTargetDirectory";
        if (dirs.Count > 3)
            return "Backup.TooManyDirectories";

        return null;
    }

    /// <summary>Непустые (обрезанные) каталоги назначения формы.</summary>
    public List<string> NonEmptyDirectories =>
        TargetDirectories
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(d => d.Trim())
            .ToList();

    /// <summary>Переносит заполненные поля формы в сценарий.</summary>
    public void ApplyTo(BackupScenario scenario)
    {
        scenario.Name = Name.Trim();
        scenario.FileNameTemplate = string.IsNullOrWhiteSpace(FileNameTemplate)
            ? "{Base}_{Timestamp}"
            : FileNameTemplate.Trim();
        scenario.Format = Format;
        scenario.Engine = Engine;
        scenario.BasePrefix = BasePrefix?.Trim() ?? "";
        scenario.IncludeTimestamp = IncludeTimestamp;
        scenario.KeepCount = Math.Max(0, KeepCount);
        scenario.DeleteOlderThanDays = Math.Max(0, DeleteOlderThanDays);
        scenario.TargetDirectories = NonEmptyDirectories;
        scenario.Credential = new BackupCredential
        {
            UseInfobaseAuth = UseInfobaseAuth,
            User = User?.Trim() ?? "",
            Password = Password ?? ""
        };
    }
}