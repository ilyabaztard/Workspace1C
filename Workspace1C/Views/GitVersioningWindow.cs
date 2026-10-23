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
    /// <summary>
    /// Окно «Версионирование конфигурации в Git» (выпуск 1.0.0.69, Windows/WPF).
    /// Логика — в <see cref="GitVersioningViewModel"/>.
    /// </summary>
    public sealed class GitVersioningWindow : Window
    {
        private readonly GitVersioningViewModel _vm;

        public GitVersioningWindow(GitVersioningViewModel vm)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            DataContext = _vm;
            WpfUi.SetupWindow(this, "Git.Title", 900, 640);

            var root = WpfUi.Rows(0, 0, -1, 0);
            root.Margin = new Thickness(16);

            var intro = WpfUi.Label("Git.Intro");
            intro.Margin = new Thickness(0, 0, 0, 10);
            WpfUi.Place(root, intro, 0);

            var form = new StackPanel();
            form.Children.Add(WpfUi.Wrap(WpfUi.Label("Git.Base"),
                WpfUi.BoundCombo(_vm.InfobaseNames, nameof(GitVersioningViewModel.SelectedIndex), 420)));
            form.Children.Add(WpfUi.Wrap(WpfUi.Label("Git.Repo"),
                WpfUi.BoundTextBox(nameof(GitVersioningViewModel.RepoPath), 420),
                WpfUi.Button("Common.Browse", (_, _) => OnBrowse(), 90)));
            form.Children.Add(WpfUi.Wrap(WpfUi.Label("Git.SubFolder"),
                WpfUi.BoundTextBox(nameof(GitVersioningViewModel.SubFolder), 160)));
            form.Children.Add(WpfUi.Wrap(WpfUi.Label("Git.Message"),
                WpfUi.BoundTextBox(nameof(GitVersioningViewModel.MessageTemplate), 420)));
            form.Children.Add(WpfUi.Wrap(WpfUi.BoundCheckBox("Git.Push", nameof(GitVersioningViewModel.Push))));
            form.Children.Add(WpfUi.Label("Git.ScheduleHint", 11));
            WpfUi.Place(root, WpfUi.Card(form), 1);

            WpfUi.Place(root, WpfUi.ReadOnlyText(nameof(GitVersioningViewModel.LogText)), 2);

            var save = WpfUi.Button("Common.Save", (_, _) => _vm.Save(), 120);
            var run = WpfUi.Button("Git.Run", async (_, _) => await _vm.RunAsync(), 220, accent: true);
            var open = WpfUi.Button("Git.OpenFolder", (_, _) => OnOpenFolder(), 140);
            foreach (var b in new[] { save, run })
                b.SetBinding(IsEnabledProperty, new Binding(nameof(GitVersioningViewModel.IsIdle)));
            var cancel = WpfUi.Button("Common.Cancel", (_, _) => _vm.Cancel(), 100);
            cancel.SetBinding(IsEnabledProperty, new Binding(nameof(GitVersioningViewModel.IsBusy)));
            var close = WpfUi.Button("Common.Close", (_, _) => Close(), 110);
            close.IsCancel = true;
            var buttons = WpfUi.ButtonRow(save, run, open, cancel, close);
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            buttons.Margin = new Thickness(0, 12, 0, 0);
            WpfUi.Place(root, buttons, 3);

            Content = root;
            Closed += (_, _) => _vm.Cancel();
        }

        private void OnBrowse()
        {
            var dialogs = AppServices.GetRequiredService<IDialogService>();
            var folder = dialogs.OpenFolderDialog(LocalizationManager.T("Git.Repo"), _vm.RepoPath);
            if (!string.IsNullOrWhiteSpace(folder))
                _vm.RepoPath = folder;
        }

        private void OnOpenFolder()
        {
            if (string.IsNullOrWhiteSpace(_vm.RepoPath) || !System.IO.Directory.Exists(_vm.RepoPath))
                return;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _vm.RepoPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _vm.AppendLog(ex.Message);
            }
        }
    }
}
#endif
