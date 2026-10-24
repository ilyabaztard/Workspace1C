#if LINUX
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Workspace1C.Localization;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>Окно «Команды Telegram-бота» (выпуск 1.0.0.69, Avalonia/Linux).</summary>
    public sealed class TelegramBotWindow : ModalWindowBase
    {
        private readonly TelegramBotViewModel _vm;

        public TelegramBotWindow(TelegramBotViewModel vm)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            DataContext = _vm;
            Title = LocalizationManager.T("Bot.Title");
            Width = 760;
            Height = 600;
            MinWidth = 600;
            MinHeight = 460;
            FontSize = 13;
            CanResize = true;

            var root = AvaloniaUi.Rows(0, 0, -1, 0, 0);
            root.Margin = new Thickness(16);

            var intro = AvaloniaUi.Label("Bot.Intro");
            intro.Margin = new Thickness(0, 0, 0, 10);
            AvaloniaUi.Place(root, intro, 0);

            var form = new StackPanel();
            form.Children.Add(AvaloniaUi.Text(_vm.TokenText, 12, secondary: true));
            form.Children.Add(AvaloniaUi.Wrap(AvaloniaUi.BoundCheckBox("Bot.Enabled", nameof(TelegramBotViewModel.Enabled))));
            form.Children.Add(AvaloniaUi.Wrap(AvaloniaUi.Label("Bot.AllowedChats"),
                AvaloniaUi.BoundTextBox(nameof(TelegramBotViewModel.AllowedChatIds), 320)));
            form.Children.Add(AvaloniaUi.Label("Bot.AllowedChatsHint"));
            form.Children.Add(AvaloniaUi.Wrap(AvaloniaUi.BoundCheckBox("Bot.AllowRun", nameof(TelegramBotViewModel.AllowRunTasks))));
            AvaloniaUi.Place(root, AvaloniaUi.Card(form), 1);

            var help = new TextBox
            {
                Text = _vm.HelpText,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true
            };
            AvaloniaUi.Place(root, help, 2);

            AvaloniaUi.Place(root, AvaloniaUi.BoundText(nameof(TelegramBotViewModel.StatusText)), 3);

            var save = AvaloniaUi.Button("Common.Save", (_, _) => _vm.Save(), 120, accent: true);
            var close = AvaloniaUi.Button("Common.Close", (_, _) => Close(), 110);
            close.IsCancel = true;
            var buttons = AvaloniaUi.ButtonRow(save, close);
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            buttons.Margin = new Thickness(0, 12, 0, 0);
            AvaloniaUi.Place(root, buttons, 4);

            Content = root;
        }
    }
}
#endif
