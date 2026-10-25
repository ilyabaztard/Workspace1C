#if LINUX
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Workspace1C.Controls;
using Workspace1C.Localization;
using Workspace1C.Themes;

namespace Workspace1C
{
    /// <summary>
    /// Диалог повтора удаления информационной базы кластера (issue #69) — Avalonia/Linux-
    /// версия WPF-окна <see cref="RacInfobaseDropRetryWindow"/>: при неудачном
    /// rac «infobase drop» (например, защищённая база) вместо простого окна с OK —
    /// текст ошибки, поля «Логин»/«Пароль» администратора ИБ и кнопки «Повторить
    /// попытку» (default) / «Отмена» (cancel). Логин сохраняется между показами
    /// диалога, пароль вводится заново. Подписи полей — кисть FormLabelBrush (issue #61).
    /// </summary>
    public sealed class RacInfobaseDropRetryWindow : ModalWindowBase
    {
        private readonly TextBox _loginBox;
        private readonly PasswordBox _passwordBox;

        /// <summary>Логин администратора ИБ для повторной попытки удаления.</summary>
        public string? User { get; private set; }

        /// <summary>Пароль администратора ИБ для повторной попытки удаления.</summary>
        public string? Password { get; private set; }

        /// <summary>True — пользователь нажал «Повторить попытку»; false — «Отмена».</summary>
        public bool Retry { get; private set; }

        /// <param name="errorText">Текст ошибки удаления («Не удалось удалить информационную
        /// базу.» + детали rac) — показывается в подписи окна.</param>
        /// <param name="login">Логин администратора ИБ с прошлой попытки (null — первый показ).</param>
        public RacInfobaseDropRetryWindow(string errorText, string? login = null)
        {
            Title = LocalizationManager.T("ServerMonitor.Infobase.Drop.RetryTitle");
            Width = 520;
            Height = 320;
            MinWidth = 480;
            MinHeight = 300;
            FontSize = 13;
            CanResize = false;

            // Поля строятся до контента — хэндлер кнопок читает их значения.
            _loginBox = new TextBox
            {
                Height = 32,
                VerticalContentAlignment = VerticalAlignment.Center,
                MaxLength = 200,
                Text = login ?? string.Empty
            }.Styled(ControlThemes.ModernTextBox);
            _passwordBox = new PasswordBox { Height = 32, MaxLength = 200 }
                .Styled(ControlThemes.ModernPasswordBox);

            Content = BuildRoot(errorText);
        }

        /// <summary>Показывает окно модально (синхронно).</summary>
        public bool ShowSync(Window? owner = null) => ShowDialogSync(owner);

        private static string T(string key) => LocalizationManager.T(key);

        private Control BuildRoot(string errorText)
        {
            var root = new DockPanel { Margin = new Thickness(16), LastChildFill = true };

            // Кнопки внизу: «Повторить попытку» (default) и «Отмена» (cancel).
            var retry = new Button
            {
                Content = T("ServerMonitor.Infobase.Drop.RetryButton"),
                Width = 150,
                Height = 36,
                IsDefault = true
            };
            retry.Styled(ControlThemes.DialogConfirmButton);
            retry.Click += (_, _) => OnRetry();
            var cancel = new Button
            {
                Content = T("ServerMonitor.Infobase.Drop.CancelButton"),
                Width = 130,
                Height = 36,
                IsCancel = true
            };
            cancel.Styled(ControlThemes.ModernButton);
            ThemeBrushes.Bind(cancel, Button.BackgroundProperty, "ItemHoverBrush");
            cancel.Click += (_, _) =>
            {
                DialogResult = false;
                Close();
            };
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 10,
                Margin = new Thickness(0, 12, 0, 0),
                Children = { retry, cancel }
            };
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            // Подпись: «Не удалось удалить информационную базу. <детали ошибки rac>».
            var error = AvaloniaUi.Text(
                string.IsNullOrWhiteSpace(errorText) ? "—" : errorText, fontSize: 14, bold: true);
            error.Margin = new Thickness(0, 0, 0, 6);

            // Пояснение: что ввести и зачем (issue #69).
            var prompt = AvaloniaUi.Text(T("ServerMonitor.Infobase.Drop.RetryPrompt"), fontSize: 12, secondary: true);
            prompt.Margin = new Thickness(0, 0, 0, 4);

            // Подписи полей — кисть FormLabelBrush (issue #61): всегда контрастны в обеих
            // темах, включая стенерированные схемы (аналог AvaloniaUi.Label).
            var loginLabel = AvaloniaUi.Label("ServerMonitor.Infobase.Drop.RetryLogin");
            loginLabel.Margin = new Thickness(0, 8, 0, 2);
            var passwordLabel = AvaloniaUi.Label("ServerMonitor.Infobase.Drop.RetryPassword");
            passwordLabel.Margin = new Thickness(0, 8, 0, 2);

            var form = new StackPanel
            {
                Children = { error, prompt, loginLabel, _loginBox, passwordLabel, _passwordBox }
            };
            root.Children.Add(form);
            return root;
        }

        private void OnRetry()
        {
            User = _loginBox.Text;
            Password = _passwordBox.Password ?? string.Empty;
            Retry = true;
            DialogResult = true;
            Close();
        }
    }
}
#endif