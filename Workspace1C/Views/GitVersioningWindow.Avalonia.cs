#if LINUX
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Workspace1C.Localization;
using Workspace1C.Services;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Версионирование конфигурации в Git» (выпуск 1.0.0.69, Avalonia/Linux).
    /// Логика — в <see cref="GitVersioningViewModel"/>.
    /// </summary>
    public sealed class GitVersioningWindow : ModalWindowBase
    {
        private readonly GitVersioningViewModel _vm;

        public GitVersioningWindow(GitVersioningViewModel vm)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            DataContext = _vm;
            Title = LocalizationManager.T("Git.Title");
            Width = 900;
            Height = 640;
            MinWidth = 700;
            MinHeight = 480;
            FontSize = 13;
            CanResize = true;

            var root = AvaloniaUi.Rows(0, 0, -1, 0);
            root.Margin = new Thickness(16);

            var intro = AvaloniaUi.Label("Git.Intro");
            intro.Margin = new Thickness(0, 0, 0, 10);
            AvaloniaUi.Place(root, intro, 0);

            var form = new StackPanel();
            form.Children.Add(AvaloniaUi.Wrap(AvaloniaUi.Label("Git.Base"),
                AvaloniaUi.BoundCombo(_vm.InfobaseNames, nameof(GitVersioningViewModel.SelectedIndex), 420)));
            form.Children.Add(AvaloniaUi.Wrap(AvaloniaUi.Label("Git.Repo"),
                AvaloniaUi.BoundTextBox(nameof(GitVersioningViewModel.RepoPath), 420),
                AvaloniaUi.Button("Common.Browse", (_, _) => OnBrowse(), 90)));
            form.Children.Add(AvaloniaUi.Wrap(AvaloniaUi.Label("Git.SubFolder"),
                AvaloniaUi.BoundTextBox(nameof(GitVersioningViewModel.SubFolder), 160)));
            form.Children.Add(AvaloniaUi.Wrap(AvaloniaUi.Label("Git.Message"),
                AvaloniaUi.BoundTextBox(nameof(GitVersioningViewModel.MessageTemplate), 420)));
            form.Children.Add(AvaloniaUi.Wrap(AvaloniaUi.BoundCheckBox("Git.Push", nameof(GitVersioningViewModel.Push))));
            form.Children.Add(AvaloniaUi.Label("Git.ScheduleHint", 11));
            AvaloniaUi.Place(root, AvaloniaUi.Card(form), 1);

            AvaloniaUi.Place(root, AvaloniaUi.ReadOnlyText(nameof(GitVersioningViewModel.LogText)), 2);

            var save = AvaloniaUi.Button("Common.Save", (_, _) => _vm.Save(), 120);
            var run = AvaloniaUi.Button("Git.Run", async (_, _) => await _vm.RunAsync(), 220, accent: true);
            foreach (var b in new[] { save, run })
                b.Bind(IsEnabledProperty, new Binding(nameof(GitVersioningViewModel.IsIdle)));
            var cancel = AvaloniaUi.Button("Common.Cancel", (_, _) => _vm.Cancel(), 100);
            cancel.Bind(IsEnabledProperty, new Binding(nameof(GitVersioningViewModel.IsBusy)));
            var close = AvaloniaUi.Button("Common.Close", (_, _) => Close(), 110);
            close.IsCancel = true;
            var buttons = AvaloniaUi.ButtonRow(save, run, cancel, close);
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            buttons.Margin = new Thickness(0, 12, 0, 0);
            AvaloniaUi.Place(root, buttons, 3);

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
    }
}
#endif
