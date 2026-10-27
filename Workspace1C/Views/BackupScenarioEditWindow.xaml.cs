#if WINDOWS
using System.Windows;
using System.Windows.Threading;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;

namespace Workspace1C;

/// <summary>
/// Окно создания/редактирования сценария резервирования (Windows/WPF).
/// Возвращает результат через свойство <see cref="Result"/> при подтверждении.
/// </summary>
public partial class BackupScenarioEditWindow : Window
{
    private readonly BackupScenarioEditViewModel _vm;
    private readonly IDialogService _dialogs;

    /// <summary>Готовый сценарий при подтверждении, иначе <c>null</c>.</summary>
    public BackupScenario? Result { get; private set; }

    /// <param name="scenario">Редактируемый сценарий или <c>null</c> для нового.</param>
    public BackupScenarioEditWindow(BackupScenario? scenario = null)
    {
        InitializeComponent();
        _dialogs = AppServices.GetRequiredService<IDialogService>();
        _vm = new BackupScenarioEditViewModel(scenario);
        DataContext = _vm;

        Title = T(scenario is null ? "Backup.AddScenario" : "Backup.EditScenario");
        NameLabel.Text = T("Backup.Name");
        TemplateLabel.Text = T("Backup.FileNameTemplate");
        TemplateHint.Text = T("Backup.TemplateHint");
        TimestampCheck.Content = T("Backup.IncludeTimestamp");
        FormatLabel.Text = T("Backup.Format");
        EngineLabel.Text = T("Backup.Engine");
        EngineHint.Text = T("Backup.EngineHint");
        PrefixLabel.Text = T("Backup.BasePrefix");
        DirsLabel.Text = T("Backup.TargetDirectories");
        AddDirButton.Content = T("Common.Browse");
        RemoveDirButton.Content = T("Common.Delete");
        RotationLabel.Text = T("Backup.Rotation");
        KeepCountLabel.Text = T("Backup.KeepCount");
        AgeDaysLabel.Text = T("Backup.DeleteOlderThanDays");
        RotationHint.Text = T("Backup.RotationHint");
        CredLabel.Text = T("Backup.Credentials");
        UseAuthCheck.Content = T("Backup.UseInfobaseAuth");
        UserLabel.Text = T("Backup.User");
        PasswordLabel.Text = T("Backup.Password");
        OkButton.Content = T("Common.Save");

        FormatCombo.ItemsSource = System.Enum.GetValues<BackupFormat>();
        EngineCombo.ItemsSource = BackupScenarioEditViewModel.EngineNames;
        EngineCombo.SelectedIndex = _vm.EngineIndex;
        PasswordBox.Password = _vm.Password;

        // Фокус в поле «Наименование» (issue #299): отложенный вызов после показа окна —
        // иначе при ShowDialog() фокус «съедается» до активации окна.
        Loaded += (_, _) =>
        {
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                NameBox.Focus();
                if (scenario is null)
                    NameBox.SelectAll();
            }));
        };
    }

    private static string T(string key) => LocalizationManager.T(key);

    private void AddDir_Click(object sender, RoutedEventArgs e)
    {
        var path = _dialogs.OpenFolderDialog(T("Backup.TargetDirectories"));
        if (!string.IsNullOrWhiteSpace(path))
            _vm.TargetDirectories.Add(path);
    }

    private void RemoveDir_Click(object sender, RoutedEventArgs e)
    {
        if (DirList.SelectedIndex >= 0)
            _vm.RemoveDirectoryAt(DirList.SelectedIndex);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        _vm.Password = PasswordBox.Password;
        _vm.EngineIndex = System.Math.Max(0, EngineCombo.SelectedIndex);
        // Числовые поля ротации: привязка к int не срабатывает на некорректном
        // тексте, поэтому парсим явно и кладём в модель (0 — хранить всё / не чистить).
        _vm.KeepCount = TryParseInt(KeepCountBox.Text, 0);
        _vm.DeleteOlderThanDays = TryParseInt(AgeDaysBox.Text, 0);
        var errorKey = _vm.Validate();
        if (errorKey is not null)
        {
            _dialogs.ShowWarning(T(errorKey), Title);
            return;
        }

        var scenario = new BackupScenario();
        _vm.ApplyTo(scenario);
        Result = scenario;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>Парсит целое число; при неудаче возвращает значение по умолчанию.</summary>
    private static int TryParseInt(string? text, int fallback) =>
        int.TryParse(text?.Trim(), out var value) && value >= 0 ? value : fallback;
}
#endif