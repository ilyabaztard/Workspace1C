#if LINUX
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Workspace1C.Controls;
using Workspace1C.Localization;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Диалог повтора удаления информационной базы кластера (issue #69) — Avalonia/Linux-
    /// версия WPF-окна <see cref="RacInfobaseDropRetryWindow"/>: при неудачном
    /// rac «infobase drop» (например, защищённая база) вместо простого окна с OK —
    /// текст ошибки, поля «Логин»/«Пароль» администратора ИБ и кнопки «Повторить
    /// попытку» (default) / «Отмена» (cancel). Логин и пароль сохраняются между
    /// показами диалога (комментарий 7OH от 2026-10-26). Подписи полей — кисть
    /// FormLabelBrush (issue #61).
    /// </summary>
    public sealed class RacInfobaseDropRetryWindow : ModalWindowBase
    {
        private readonly TextBox _loginBox;
        private readonly PasswordBox _passwordBox;
        private readonly CheckBox _blockScheduledJobsCheckBox;

        /// <summary>Логин администратора ИБ для повторной попытки удаления.</summary>
        public string? User { get; private set; }

        /// <summary>Пароль администратора ИБ для повторной попытки удаления.</summary>
        public string? Password { get; private set; }

        /// <summary>True — пользователь нажал «Повторить попытку»; false — «Отмена».</summary>
        public bool Retry { get; private set; }

        /// <summary>True — пользователь нажал «Завершить сеансы и повторить»: перед
        /// повторным «infobase drop» завершить активные сеансы/соединения базы.</summary>
        public bool TerminateSessions { get; private set; }

        /// <summary>True — пользователь поставил галочку «Установить блокировку регламентных
        /// заданий» (issue #69, комментарий 7OH от 2026-10-26): перед завершением сеансов
        /// сначала выполняется rac «infobase update --scheduled-jobs-deny=on».</summary>
        public bool BlockScheduledJobs { get; private set; }

        /// <param name="errorText">Текст ошибки удаления («Не удалось удалить информационную
        /// базу.» + детали rac) — показывается в подписи окна.</param>
        /// <param name="login">Логин администратора ИБ с прошлой попытки (null — первый показ).</param>
        /// <param name="password">Пароль администратора ИБ с прошлой попытки (null — первый
        /// показ): сохраняется между попытками (комментарий 7OH от 2026-10-26).</param>
        public RacInfobaseDropRetryWindow(string errorText, string? login = null, string? password = null)
        {
            Title = LocalizationManager.T("ServerMonitor.Infobase.Drop.RetryTitle");
            Width = 520;
            Height = 440;
            MinWidth = 480;
            MinHeight = 400;
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
            _passwordBox = new PasswordBox
            {
                Height = 32,
                MaxLength = 200,
                Password = password ?? string.Empty
            }.Styled(ControlThemes.ModernPasswordBox);
            // issue #69, комментарий 7OH от 2026-10-26: галочка «Установить блокировку
            // регламентных заданий» — видна с ПЕРВОГО показа диалога (сразу при запросе
            // логина/пароля); при отмеченной галочке перед завершением сеансов/повторным
            // «infobase drop» сначала ставится блокировка (--scheduled-jobs-deny=on),
            // затем снимаются сеансы.
            _blockScheduledJobsCheckBox = new CheckBox
            {
                Content = T("ServerMonitor.Infobase.Drop.BlockScheduledJobs"),
                Margin = new Thickness(0, 10, 0, 0)
            }.Styled(ControlThemes.CacheCleanCheckBox);

            Content = BuildRoot(errorText);
        }

        /// <summary>Показывает окно модально (синхронно).</summary>
        public bool ShowSync(Window? owner = null) => ShowDialogSync(owner);

        private static string T(string key) => LocalizationManager.T(key);

        private Control BuildRoot(string errorText)
        {
            var root = new DockPanel { Margin = new Thickness(16), LastChildFill = true };

            // issue #69, комментарий 7OH от 2026-10-26: типовую ошибку rac классифицируем
            // и по ней показываем подсказку; базу, занятую сеансами, можно освободить
            // кнопкой «Завершить сеансы и повторить» (connection disconnect + session terminate).
            var kind = ServerMonitorViewModel.ClassifyDropError(errorText);
            var hint = ServerMonitorViewModel.BuildDropRetryHint(kind);

            // Кнопки внизу: «Завершить сеансы и повторить» (только при занятой базе),
            // «Повторить попытку» (default) и «Отмена» (cancel).
            // Повтор удаления базы кластера — опасное действие: DangerButton (issue #26).
            var retry = new Button
            {
                Content = T("ServerMonitor.Infobase.Drop.RetryButton"),
                Width = 150,
                Height = 36,
                IsDefault = true
            };
            retry.Styled(ControlThemes.DangerButton);
            retry.Click += (_, _) => OnRetry();
            var cancel = new Button
            {
                Content = T("ServerMonitor.Infobase.Drop.CancelButton"),
                Width = 130,
                Height = 36,
                IsCancel = true
            };
            cancel.Styled(ControlThemes.SecondaryButton);
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
            if (kind == ServerMonitorViewModel.RacDropErrorKind.Sessions)
            {
                var terminate = new Button
                {
                    Content = T("ServerMonitor.Infobase.Drop.TerminateSessionsButton"),
                    Height = 36,
                    IsDefault = false
                };
                terminate.Styled(ControlThemes.ModernButton);
                ThemeBrushes.Bind(terminate, Button.BackgroundProperty, "ItemHoverBrush");
                terminate.Click += (_, _) => OnTerminateSessions();
                buttons.Children.Insert(0, terminate);
            }
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            // Подпись: «Не удалось удалить информационную базу. <детали ошибки rac>» либо
            // «Повторная попытка удаления не удалась: <детали rac>».
            var error = AvaloniaUi.Text(
                string.IsNullOrWhiteSpace(errorText) ? "—" : errorText, fontSize: 14, bold: true);
            error.Margin = new Thickness(0, 0, 0, 6);

            // Пояснение: что ввести и зачем (issue #69).
            var prompt = AvaloniaUi.Text(T("ServerMonitor.Infobase.Drop.RetryPrompt"), fontSize: 12, secondary: true);
            prompt.Margin = new Thickness(0, 0, 0, 4);

            var form = new StackPanel { Children = { error, prompt } };
            if (!string.IsNullOrEmpty(hint))
            {
                var suggestion = AvaloniaUi.Text(hint, fontSize: 12, secondary: true);
                suggestion.Margin = new Thickness(0, 0, 0, 4);
                form.Children.Add(suggestion);
            }

            // Подписи полей — кисть FormLabelBrush (issue #61): всегда контрастны в обеих
            // темах, включая стенерированные схемы (аналог AvaloniaUi.Label).
            var loginLabel = AvaloniaUi.Label("ServerMonitor.Infobase.Drop.RetryLogin");
            loginLabel.Margin = new Thickness(0, 8, 0, 2);
            var passwordLabel = AvaloniaUi.Label("ServerMonitor.Infobase.Drop.RetryPassword");
            passwordLabel.Margin = new Thickness(0, 8, 0, 2);
            form.Children.Add(loginLabel);
            form.Children.Add(_loginBox);
            form.Children.Add(passwordLabel);
            form.Children.Add(_passwordBox);

            // Галочка блокировки регламентных заданий видна с ПЕРВОГО показа диалога —
            // сразу при запросе логина/пароля (комментарий 7OH от 2026-10-26: «И может
            // быть сразу галочку показывать - ещё при первом запросе логина и пароля?»);
            // кнопка «Завершить сеансы и повторить» (выше) — только для занятой базы.
            form.Children.Add(_blockScheduledJobsCheckBox);

            root.Children.Add(form);
            return root;
        }

        private void OnTerminateSessions()
        {
            User = _loginBox.Text;
            Password = _passwordBox.Password ?? string.Empty;
            Retry = true;
            TerminateSessions = true;
            BlockScheduledJobs = _blockScheduledJobsCheckBox.IsChecked == true;
            DialogResult = true;
            Close();
        }

        private void OnRetry()
        {
            User = _loginBox.Text;
            Password = _passwordBox.Password ?? string.Empty;
            Retry = true;
            BlockScheduledJobs = _blockScheduledJobsCheckBox.IsChecked == true;
            DialogResult = true;
            Close();
        }
    }
}
#endif