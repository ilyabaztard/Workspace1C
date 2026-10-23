#if LINUX
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Workspace1C.Localization;
using Workspace1C.Services;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>Окно «Общие списки баз» (выпуск 1.0.0.69, Avalonia/Linux).</summary>
    public sealed class SharedListsWindow : ModalWindowBase
    {
        private static readonly double[] Columns = { 40, -1, -3 };

        private readonly SharedListsViewModel _vm;

        public SharedListsWindow(SharedListsViewModel vm)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            DataContext = _vm;
            Title = LocalizationManager.T("Shared.Title");
            Width = 960;
            Height = 600;
            MinWidth = 720;
            MinHeight = 440;
            FontSize = 13;
            CanResize = true;

            var root = AvaloniaUi.Rows(0, 0, 0, -1, 0, 0, 0);
            root.Margin = new Thickness(16);

            var intro = AvaloniaUi.Label("Shared.Intro");
            intro.Margin = new Thickness(0, 0, 0, 10);
            AvaloniaUi.Place(root, intro, 0);

            AvaloniaUi.Place(root, AvaloniaUi.Wrap(
                AvaloniaUi.Button("Shared.AddFile", (_, _) => OnAddFile(), 150),
                AvaloniaUi.Button("Shared.AddUrl", (_, _) => _vm.AddSource("https://"), 150),
                AvaloniaUi.Button("Common.Delete", (_, _) => _vm.RemoveSelected(), 110),
                AvaloniaUi.Label("Shared.Interval"), AvaloniaUi.BoundTextBox(nameof(SharedListsViewModel.IntervalText), 60)), 1);

            AvaloniaUi.Place(root, AvaloniaUi.Header(new[] { "Alerts.ColOn", "Shared.ColName", "Shared.ColLocation" }, Columns), 2);
            var list = new ListBox { ItemsSource = _vm.Sources };
            list.ItemTemplate = new FuncDataTemplate<SharedListSourceRow>((_, _) =>
            {
                var grid = new Grid { ColumnDefinitions = AvaloniaUi.Columns(Columns) };
                grid.Children.Add(AvaloniaUi.CellCheck(nameof(SharedListSourceRow.Enabled), 0));
                grid.Children.Add(AvaloniaUi.CellBox(nameof(SharedListSourceRow.Name), 1));
                grid.Children.Add(AvaloniaUi.CellBox(nameof(SharedListSourceRow.Location), 2));
                return grid;
            });
            list.Bind(SelectingItemsControl.SelectedItemProperty,
                new Binding(nameof(SharedListsViewModel.Selected)) { Mode = BindingMode.TwoWay });
            AvaloniaUi.Place(root, list, 3);

            AvaloniaUi.Place(root, AvaloniaUi.Label("Shared.Hint", 11), 4);
            AvaloniaUi.Place(root, AvaloniaUi.BoundText(nameof(SharedListsViewModel.StatusText)), 5);

            var save = AvaloniaUi.Button("Common.Save", (_, _) => _vm.Save(), 120);
            var sync = AvaloniaUi.Button("Shared.SyncNow", async (_, _) => await _vm.SyncNowAsync(), 220, accent: true);
            sync.Bind(IsEnabledProperty, new Binding(nameof(SharedListsViewModel.IsIdle)));
            var close = AvaloniaUi.Button("Common.Close", (_, _) => Close(), 110);
            close.IsCancel = true;
            var buttons = AvaloniaUi.ButtonRow(save, sync, close);
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            buttons.Margin = new Thickness(0, 12, 0, 0);
            AvaloniaUi.Place(root, buttons, 6);

            Content = root;
        }

        private void OnAddFile()
        {
            var dialogs = AppServices.GetRequiredService<IDialogService>();
            var path = dialogs.OpenFileDialog(LocalizationManager.T("Shared.ChooseFile"),
                $"{LocalizationManager.T("Settings.Ibases.FileFilter")}|*.v8i|{LocalizationManager.T("Common.AllFiles")}|*.*");
            if (!string.IsNullOrWhiteSpace(path))
                _vm.AddSource(path);
        }
    }
}
#endif
