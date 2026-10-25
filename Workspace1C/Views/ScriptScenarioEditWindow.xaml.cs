#if WINDOWS
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;

namespace Workspace1C;

/// <summary>
/// Окно создания/редактирования сценария запуска скрипта (Windows/WPF).
/// Возвращает результат через свойство <see cref="Result"/> при подтверждении.
/// Параметры задаются строками (каждая строка — отдельный параметр); двойной клик
/// по токену подстановки вставляет его в позицию курсора поля параметров.
/// </summary>
public partial class ScriptScenarioEditWindow : Window
{
    private readonly ScriptScenarioEditViewModel _vm;
    private readonly IDialogService _dialogs;
    private readonly List<Infobase> _infobases = new();

    /// <summary>
    /// Редактируемый сценарий (issue #308): при подтверждении поля применяются к нему же,
    /// чтобы Store.Save по тому же Id перезаписал файл, а не создавал копию. null — новый.
    /// </summary>
    private readonly ScriptScenario? _sourceScenario;

    /// <summary>Готовый сценарий при подтверждении, иначе <c>null</c>.</summary>
    public ScriptScenario? Result { get; private set; }

    /// <param name="scenario">Редактируемый сценарий или <c>null</c> для нового.</param>
    public ScriptScenarioEditWindow(ScriptScenario? scenario = null)
    {
        InitializeComponent();
        _dialogs = AppServices.GetRequiredService<IDialogService>();
        _sourceScenario = scenario;
        _vm = new ScriptScenarioEditViewModel(scenario);
        DataContext = _vm;

        Title = T(scenario is null ? "Script.AddTitle" : "Script.EditTitle");
        NameLabel.Text = T("Script.Name");
        FilePathLabel.Text = T("Script.FilePath");
        BrowseButton.Content = T("Script.Browse");
        WorkingDirectoryLabel.Text = T("Script.WorkingDirectory");
        WorkingDirectoryBrowseButton.Content = T("Script.Browse");
        // Issue #308, п.9: «Интерпретатор» — выбор shell запуска сценария.
        // Значения enum отображаются локализованными подписями через KeyValuePair.
        ShellLabel.Text = T("Script.Shell");
        ShellCombo.ItemsSource = ScriptScenarioEditViewModel.ShellOptions
            .Select(s => new KeyValuePair<ScriptShell, string>(s, ShellDisplayName(s)))
            .ToList();
        ShellCombo.SelectedValue = _vm.Shell;
        ParametersLabel.Text = T("Script.Parameters");
        ParametersHint.Text = T("Script.ParametersHint");
        TokensLabel.Text = T("Script.Tokens");
        PickTokenButton.Content = T("Script.PickButton");
        TokensHint.Text = T("Script.TokensHint");
        ExampleBaseLabel.Text = T("Script.ExampleBase");
        PreviewLabel.Text = T("Script.CommandLinePreview");
        OkButton.Content = T("Common.Save");
        // Галки «Уведомлять о запуске» и «Не закрывать» (issue #308) влияют на превью
        // командной строки (хвост pause/Read-Host/read) — пересчёт по клику.
        NotifyOnStartCheckBox.Checked += (_, _) => UpdatePreview();
        NotifyOnStartCheckBox.Unchecked += (_, _) => UpdatePreview();
        KeepOpenCheckBox.Checked += (_, _) => UpdatePreview();
        KeepOpenCheckBox.Unchecked += (_, _) => UpdatePreview();

        // Токены подстановок: объекты ScriptTokenHint (issue #308) — в шаблоне списка
        // отображается «%token% — описание», а двойной клик вставляет ТОЛЬКО токен.
        TokensList.ItemsSource = ScriptScenarioEditViewModel.AvailableTokens
            .Select(t => new ScriptTokenHint(t.Token, t.LocalizationKey, T(t.LocalizationKey)))
            .ToList();

        // Базы для живого примера командной строки. Имя базы выводит явный ItemTemplate из
        // XAML (issue #308): DisplayMemberPath, заданный из code-behind, применялся после
        // первой отрисовки, и закрытый комбобокс показывал ToString() (имя типа Infobase).
        try
        {
            _infobases.AddRange(AppServices.GetRequiredService<IInfobaseRepository>().Load());
        }
        catch
        {
            // Окно редактирования работает и без списка баз — пример просто не покажется.
        }
        ExampleBaseCombo.ItemsSource = _infobases;
        // Выбор первой базы восстанавливается после Loaded (см. ниже): превью строится
        // по первой базе, поэтому комбобокс должен показывать её же.

        Loaded += (_, _) =>
        {
            // Фокус в поле «Наименование» (issue #299): отложенный вызов после показа окна —
            // иначе при ShowDialog() фокус «съедается» до активации окна.
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                NameBox.Focus();
                if (scenario is null)
                    NameBox.SelectAll();
            }));

            // Переустановка выбора первой базы ПОСЛЕ показа окна (issue #308, п.14:37):
            // закрытый комбобокс рисует выбранный элемент до применения шаблона и мог
            // показать ToString() (имя типа); повторный SelectedIndex пересоздаёт контейнер
            // с применённым ItemTemplate, и элемент отображается именем базы.
            if (_infobases.Count > 0)
                ExampleBaseCombo.SelectedIndex = 0;

            UpdatePreview();
            // Высота окна редактора (issue #308): чуть выше, чтобы всё влезало без
            // скролла, но не за рамки экрана — WindowSizeMath (FitTop/clamping), как
            // в остальных окнах (ConfigDiffSetupWindow, CreateInfobaseWindow).
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, FitHeightToContent);
        };
    }

    /// <summary>
    /// Подгоняет высоту окна под содержимое (issue #308): измеряет корневой контейнер
    /// при бесконечной высоте, клампит в [MinHeight, MaxHeight] через
    /// <see cref="WindowSizeMath.ClampHeight"/> и поднимает окно, если низ уходит
    /// за нижний край рабочей области (<see cref="WindowSizeMath.FitTop"/>).
    /// </summary>
    private void FitHeightToContent()
    {
        if (RootPanel is null || !IsLoaded || !IsVisible)
            return;

        var availableWidth = RootPanel.ActualWidth > 0 ? RootPanel.ActualWidth : Math.Max(400, Width);
        RootPanel.Measure(new System.Windows.Size(availableWidth, double.PositiveInfinity));
        var desired = RootPanel.DesiredSize.Height;
        if (desired <= 0)
            return;

        // Хром (заголовок окна + рамки) — разница между полной высотой и клиентской областью.
        var chrome = Math.Max(0, ActualHeight - (RootPanel.ActualHeight > 0 ? RootPanel.ActualHeight : desired));
        var target = WindowSizeMath.ClampHeight(desired + chrome, MinHeight, MaxHeight);
        if (Math.Abs(target - Height) > 1)
            Height = target;

        // Окно стояло у нижнего края экрана и выросло — поднимаем его, чтобы нижняя
        // часть не уходила за экран (issue #308).
        var wa = SystemParameters.WorkArea;
        var newTop = WindowSizeMath.FitTop(Top, Height, wa.Top, wa.Bottom);
        if (Math.Abs(newTop - Top) > 1)
            Top = newTop;
    }

    private static string T(string key) => LocalizationManager.T(key);

    /// <summary>Локализованная подпись интерпретатора для выпадающего списка
    /// (issue #308, п.9): «Авто» локализуется, имена инструментов фиксированы.</summary>
    private static string ShellDisplayName(ScriptShell shell) => shell switch
    {
        ScriptShell.Cmd => "cmd",
        ScriptShell.PowerShell => "PowerShell",
        ScriptShell.Sh => "sh",
        _ => T("Script.Shell.Auto")
    };

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var path = _dialogs.OpenFileDialog(T("Script.FilePath"));
        if (!string.IsNullOrWhiteSpace(path))
        {
            FilePathBox.Text = path;
            UpdatePreview();
        }
    }

    /// <summary>
    /// Выбор «Папки запуска» сценария (issue #308, п.7): открывает диалог выбора
    /// каталога; пустое значение — наследовать рабочий каталог приложения.
    /// </summary>
    private void BrowseWorkingDirectory_Click(object sender, RoutedEventArgs e)
    {
        var path = _dialogs.OpenFolderDialog(T("Script.WorkingDirectory"));
        if (!string.IsNullOrWhiteSpace(path))
        {
            WorkingDirectoryBox.Text = path;
            UpdatePreview();
        }
    }

    private void TokensList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Двойной клик вставляет ТОЛЬКО токен (issue #308), а не строку «%token% — описание»;
        // guard по ScriptTokenHint обязателен — двойной клик возможен и по пустому месту списка.
        if (TokensList.SelectedItem is not ScriptTokenHint hint || string.IsNullOrEmpty(hint.Token))
            return;
        var token = hint.Token;
        // Вставка токена в позицию курсора поля параметров (план #308: «двойной клик
        // по параметру вставляет его в позицию курсора поля параметров»).
        var text = ParametersBox.Text ?? "";
        var caret = ParametersBox.CaretIndex;
        if (caret > text.Length)
            caret = text.Length;
        ParametersBox.Text = text.Insert(caret, token);
        ParametersBox.CaretIndex = caret + token.Length;
        ParametersBox.Focus();
        UpdatePreview();
    }

    /// <summary>
    /// «Выбрать…» (issue #67): открывает диалог со ВСЕМИ переменными базы (имя — значение,
    /// поиск); по «Ок»/двойному клику вставка выбранного токена в поле параметров.
    /// </summary>
    private void OnPickToken_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ScriptTokenPickerWindow(SelectedExampleBase)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.SelectedToken))
            return;

        var token = dialog.SelectedToken!;
        var text = ParametersBox.Text ?? "";
        var caret = ParametersBox.CaretIndex;
        if (caret > text.Length)
            caret = text.Length;
        ParametersBox.Text = text.Insert(caret, token);
        ParametersBox.CaretIndex = caret + token.Length;
        ParametersBox.Focus();
        UpdatePreview();
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdatePreview();

    private Infobase? SelectedExampleBase =>
        ExampleBaseCombo.SelectedItem is Infobase ib ? ib
        : ExampleBaseCombo.Items.Count > 0 ? _infobases.FirstOrDefault()
        : null;

    private void UpdatePreview()
    {
        // Выбранный интерпретатор (issue #308, п.9) попадает и в VM, и в превью.
        _vm.Shell = ShellCombo.SelectedValue is ScriptShell shell ? shell : _vm.Shell;
        var draft = new ScriptScenario
        {
            Name = _vm.Name,
            FilePath = FilePathBox.Text ?? "",
            WorkingDirectory = WorkingDirectoryBox.Text ?? "",
            Parameters = _vm.NonEmptyParameters,
            Shell = _vm.Shell,
            // «Не закрывать» (issue #308) попадает в превью: хвост pause/Read-Host/read
            // виден в командной строке окна редактора.
            KeepOpen = KeepOpenCheckBox.IsChecked ?? false
        };
        var preview = ScriptScenarioEditViewModel.BuildExampleCommandLine(draft, SelectedExampleBase);
        PreviewBox.Text = string.IsNullOrWhiteSpace(preview) ? "—" : preview;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        _vm.Name = NameBox.Text ?? "";
        _vm.FilePath = FilePathBox.Text ?? "";
        _vm.WorkingDirectory = WorkingDirectoryBox.Text ?? "";
        _vm.ParametersText = ParametersBox.Text ?? "";
        _vm.HideWindow = HideWindowCheckBox.IsChecked ?? true;
        _vm.NotifyOnStart = NotifyOnStartCheckBox.IsChecked ?? false;
        _vm.KeepOpen = KeepOpenCheckBox.IsChecked ?? false;
        _vm.Shell = ShellCombo.SelectedValue is ScriptShell shell ? shell : ScriptShell.Auto;

        var errorKey = _vm.Validate();
        if (errorKey is not null)
        {
            _dialogs.ShowWarning(T(errorKey), Title);
            return;
        }

        // Issue #308: при редактировании поля применяются к переданному сценарию (Id сохраняется),
        // иначе создаётся новый — Store.Save по тому же Id перезапишет файл без дублей.
        if (_sourceScenario is not null)
        {
            _vm.ApplyTo(_sourceScenario);
            Result = _sourceScenario;
        }
        else
        {
            var created = new ScriptScenario();
            _vm.ApplyTo(created);
            Result = created;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
#endif