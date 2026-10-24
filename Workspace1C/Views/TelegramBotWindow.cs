#if WINDOWS
using System;
using System.Windows;
using System.Windows.Controls;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>Окно «Команды Telegram-бота» (выпуск 1.0.0.69, Windows/WPF).</summary>
    public sealed class TelegramBotWindow : Window
    {
        private readonly TelegramBotViewModel _vm;

        public TelegramBotWindow(TelegramBotViewModel vm)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            DataContext = _vm;
            WpfUi.SetupWindow(this, "Bot.Title", 760, 600);

            var root = WpfUi.Rows(0, 0, -1, 0, 0);
            root.Margin = new Thickness(16);

            var intro = WpfUi.Label("Bot.Intro");
            intro.Margin = new Thickness(0, 0, 0, 10);
            WpfUi.Place(root, intro, 0);

            var form = new StackPanel();
            form.Children.Add(WpfUi.Text(_vm.TokenText, 12, secondary: true));
            form.Children.Add(WpfUi.Wrap(WpfUi.BoundCheckBox("Bot.Enabled", nameof(TelegramBotViewModel.Enabled))));
            form.Children.Add(WpfUi.Wrap(WpfUi.Label("Bot.AllowedChats"),
                WpfUi.BoundTextBox(nameof(TelegramBotViewModel.AllowedChatIds), 320)));
            form.Children.Add(WpfUi.Label("Bot.AllowedChatsHint"));
            form.Children.Add(WpfUi.Wrap(WpfUi.BoundCheckBox("Bot.AllowRun", nameof(TelegramBotViewModel.AllowRunTasks))));
            WpfUi.Place(root, WpfUi.Card(form), 1);

            var help = new TextBox
            {
                Text = _vm.HelpText,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(8)
            };
            help.SetResourceReference(Control.BackgroundProperty, "CardBackgroundBrush");
            help.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
            WpfUi.Place(root, help, 2);

            WpfUi.Place(root, WpfUi.BoundText(nameof(TelegramBotViewModel.StatusText)), 3);

            var save = WpfUi.Button("Common.Save", (_, _) => _vm.Save(), 120, accent: true);
            var close = WpfUi.Button("Common.Close", (_, _) => Close(), 110);
            close.IsCancel = true;
            var buttons = WpfUi.ButtonRow(save, close);
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            buttons.Margin = new Thickness(0, 12, 0, 0);
            WpfUi.Place(root, buttons, 4);

            Content = root;
        }
    }
}
#endif
