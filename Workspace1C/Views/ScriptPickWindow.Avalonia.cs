#if LINUX
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C;

/// <summary>
/// Окно «Выбор скрипта» (Avalonia/Linux, issue #308): список сценариев запуска скриптов
/// с живой подсказкой полной командной строки (подстановки выполнены для выбранной
/// базы). Запуск — двойным кликом или кнопкой «Выполнить».
/// </summary>
public sealed class ScriptPickWindow : ModalWindowBase
{
    private readonly IScriptScenarioStore _store = AppServices.GetRequiredService<IScriptScenarioStore>();
    private readonly Infobase _infobase;
    private readonly MainViewModel _vm;
    private readonly ListBox _list = new();
    private readonly Button _runButton = new();
    private readonly Button _editButton = new();
    private readonly TextBox _previewBox = new TextBox { IsReadOnly = true }.Styled(ControlThemes.ModernTextBox);

    public ScriptPickWindow(Infobase infobase, MainViewModel vm)
    {
        _infobase = infobase;
        _vm = vm;
        Title = T("Script.PickTitle");
        Width = 680;
        Height = 480;
        MinWidth = 560;
        MinHeight = 400;
        FontSize = 13;
        Content = BuildRoot();
        Reload();

        // issue #308: активным при открытии делаем список — сценарий можно сразу
        // выбирать курсором. Первый пункт выбираем, только если ничего не выбрано
        // (повторный Reload после правки сохраняет восстановленный выбор).
        Opened += (_, _) =>
        {
            if (_list.ItemCount > 0 && _list.SelectedIndex < 0)
                _list.SelectedIndex = 0;
            FocusList();
        };
    }

    /// <summary>
    /// Передаёт клавиатурный фокус списку сценариев (issue #308). На Opened окно может быть
    /// ещё неактивным — повторяем перенос фокуса после первой отрисовки, фокусируя и контейнер
    /// выбранной строки, чтобы стрелки двигали выбор сразу, без клика мышью.
    /// </summary>
    private void FocusList()
    {
        _list.Focus(NavigationMethod.Pointer);
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!IsVisible)
                return;
            _list.Focus(NavigationMethod.Pointer);
            if (_list.ContainerFromIndex(_list.SelectedIndex) is { } container)
                container.Focus(NavigationMethod.Pointer);
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    /// <summary>Показывает окно модально (синхронно).</summary>
    public bool ShowSync(Window? owner = null) => ShowDialogSync(owner);

    private static string T(string key) => LocalizationManager.T(key);

    private Control BuildRoot()
    {
        var dock = new DockPanel { Margin = new Avalonia.Thickness(14) };

        var bottom = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Avalonia.Thickness(0, 12, 0, 0)
        };
        _runButton.Content = T("Script.RunShort");
        _runButton.IsEnabled = false;
        _runButton.Click += async (_, _) => await RunSelectedAsync();
        _editButton.Content = T("Common.Edit");
        _editButton.IsEnabled = false;
        _editButton.Click += (_, _) => EditSelected();
        var close = new Button { Content = T("Common.Close") };
        close.Click += (_, _) => Close();
        foreach (var b in new Control[] { _runButton, _editButton, close })
        {
            b.Styled(ControlThemes.ModernButton);
            b.Width = 100;
        }
        bottom.Children.Add(_runButton);
        bottom.Children.Add(_editButton);
        bottom.Children.Add(close);

        var hint = new TextBlock { Text = string.Format(T("Script.PickHint"), _infobase.Name), FontSize = 11 };
        ThemeBrushes.Bind(hint, TextBlock.ForegroundProperty, "TextSecondaryColorBrush");

        _list.Margin = new Avalonia.Thickness(0, 6, 0, 8);
        _list.SelectionChanged += (_, _) =>
        {
            var has = Selected is not null;
            _runButton.IsEnabled = has;
            _editButton.IsEnabled = has;
            UpdatePreview();
        };
        _list.DoubleTapped += async (_, _) =>
        {
            if (Selected is not null)
                await RunSelectedAsync();
        };
        // Enter на выделенной строке = кнопка «Выполнить» (issue #308).
        _list.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter && Selected is not null)
            {
                e.Handled = true;
                await RunSelectedAsync();
            }
        };

        var previewLabel = new TextBlock { Text = T("Script.CommandLinePreview"), FontSize = 11 };
        ThemeBrushes.Bind(previewLabel, TextBlock.ForegroundProperty, "TextSecondaryColorBrush");
        _previewBox.TextWrapping = Avalonia.Media.TextWrapping.Wrap;

        var preview = new StackPanel { Spacing = 2 };
        preview.Children.Add(previewLabel);
        preview.Children.Add(_previewBox);

        var grid = new DockPanel();
        DockPanel.SetDock(bottom, Dock.Bottom);
        DockPanel.SetDock(hint, Dock.Top);
        DockPanel.SetDock(preview, Dock.Bottom);
        grid.Children.Add(bottom);
        grid.Children.Add(hint);
        grid.Children.Add(preview);
        grid.Children.Add(_list);

        dock.Children.Add(grid);
        return dock;
    }

    private void Reload()
    {
        _list.ItemsSource = _store.LoadAll()
            .Select(s => new ScriptScenarioItemViewModel(s))
            .ToList();
    }

    private ScriptScenarioItemViewModel? Selected => _list.SelectedItem as ScriptScenarioItemViewModel;

    private void UpdatePreview()
    {
        _previewBox.Text = Selected is { } item
            ? ScriptScenarioEditViewModel.BuildExampleCommandLine(item.Scenario, _infobase)
            : "—";
    }

    /// <summary>
    /// «Изменить» (issue #308, п.8): открывает редактор выбранного сценария; после
    /// сохранения изменения записываются в хранилище (тот же Id) и список обновляется,
    /// выбранный элемент восстанавливается — кнопки и превью соответствуют ему.
    /// </summary>
    private void EditSelected()
    {
        if (Selected is not { } item)
            return;
        // Issue #67 (п.4): «База для примера» в редакторе — база, из которой запущен
        // сценарий (_infobase), а не всегда первая в списке.
        var edit = new ScriptScenarioEditWindow(item.Scenario, _infobase);
        if (edit.ShowDialogSync(this) && edit.Result is { } updated)
        {
            _store.Save(updated);
            Reload();
            var updatedItem = _list.Items.Cast<ScriptScenarioItemViewModel>()
                .FirstOrDefault(i => i.Id == updated.Id);
            if (updatedItem is not null)
                _list.SelectedItem = updatedItem;
        }
    }

    private async System.Threading.Tasks.Task RunSelectedAsync()
    {
        if (Selected is not { } item)
            return;
        await _vm.RunScriptAsync(_infobase, item.Scenario);
        Close();
    }
}
#endif