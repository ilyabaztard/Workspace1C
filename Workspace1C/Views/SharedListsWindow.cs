#if WINDOWS
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Workspace1C.Localization;
using Workspace1C.Services;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>Окно «Общие списки баз» (выпуск 1.0.0.69, Windows/WPF).</summary>
    public sealed class SharedListsWindow : Window
    {
        private readonly SharedListsViewModel _vm;

        public SharedListsWindow(SharedListsViewModel vm)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            DataContext = _vm;
            WpfUi.SetupWindow(this, "Shared.Title", 960, 600);

            var root = WpfUi.Rows(0, 0, -1, 0, 0, 0);
            root.Margin = new Thickness(16);

            var intro = WpfUi.Label("Shared.Intro");
            intro.Margin = new Thickness(0, 0, 0, 10);
            WpfUi.Place(root, intro, 0);

            WpfUi.Place(root, WpfUi.Wrap(
                WpfUi.Button("Shared.AddFile", (_, _) => OnAddFile(), 150),
                WpfUi.Button("Shared.AddUrl", (_, _) => _vm.AddSource("https://"), 150),
                WpfUi.Button("Common.Delete", (_, _) => _vm.RemoveSelected(), 110),
                WpfUi.Label("Shared.Interval"), WpfUi.BoundTextBox(nameof(SharedListsViewModel.IntervalText), 60)), 1);

            var grid = WpfUi.Grid(_vm.Sources);
            grid.IsReadOnly = false;
            grid.Columns.Add(WpfUi.CheckColumn(nameof(SharedListSourceRow.Enabled)));
            grid.Columns.Add(WpfUi.EditColumn("Shared.ColName", nameof(SharedListSourceRow.Name), new DataGridLength(1, DataGridLengthUnitType.Star)));
            grid.Columns.Add(WpfUi.EditColumn("Shared.ColLocation", nameof(SharedListSourceRow.Location), new DataGridLength(3, DataGridLengthUnitType.Star)));
            grid.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty,
                new Binding(nameof(SharedListsViewModel.Selected)) { Mode = BindingMode.TwoWay });
            WpfUi.Place(root, grid, 2);

            WpfUi.Place(root, WpfUi.Label("Shared.Hint", 11), 3);
            WpfUi.Place(root, WpfUi.BoundText(nameof(SharedListsViewModel.StatusText)), 4);

            var save = WpfUi.Button("Common.Save", (_, _) => _vm.Save(), 120);
            var sync = WpfUi.Button("Shared.SyncNow", async (_, _) => await _vm.SyncNowAsync(), 220, accent: true);
            sync.SetBinding(IsEnabledProperty, new Binding(nameof(SharedListsViewModel.IsIdle)));
            var close = WpfUi.Button("Common.Close", (_, _) => Close(), 110);
            close.IsCancel = true;
            var buttons = WpfUi.ButtonRow(save, sync, close);
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            buttons.Margin = new Thickness(0, 12, 0, 0);
            WpfUi.Place(root, buttons, 5);

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
