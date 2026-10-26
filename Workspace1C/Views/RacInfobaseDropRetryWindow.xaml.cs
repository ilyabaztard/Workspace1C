using System.Windows;
using Workspace1C.Localization;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Диалог повтора удаления информационной базы кластера (issue #69): при неудачном
    /// rac «infobase drop» (например, защищённая база) вместо простого окна с OK —
    /// текст ошибки, поля «Логин»/«Пароль» администратора ИБ и кнопки «Повторить
    /// попытку» (default) / «Отмена» (cancel). Логин сохраняется между показами
    /// диалога, пароль вводится заново при каждой попытке. Типовая ошибка rac
    /// классифицируется (<see cref="ServerMonitorViewModel.ClassifyDropError"/>):
    /// при занятости базы активными сеансами появляется кнопка «Завершить сеансы и
    /// повторить» и подсказка, что делать дальше (комментарий 7OH от 2026-10-26).
    /// </summary>
    public partial class RacInfobaseDropRetryWindow : Window
    {
        /// <summary>Логин администратора ИБ для повторной попытки удаления.</summary>
        public string? User { get; private set; }

        /// <summary>Пароль администратора ИБ для повторной попытки удаления.</summary>
        public string? Password { get; private set; }

        /// <summary>True — пользователь нажал «Повторить попытку»; false — «Отмена».</summary>
        public bool Retry { get; private set; }

        /// <summary>True — пользователь нажал «Завершить сеансы и повторить»: перед
        /// повторным «infobase drop» завершить активные сеансы/соединения базы.</summary>
        public bool TerminateSessions { get; private set; }

        /// <param name="errorText">Текст ошибки удаления («Не удалось удалить информационную
        /// базу.» + детали rac либо «Повторная попытка... не удалась: ...») — показывается
        /// в подписи окна.</param>
        /// <param name="login">Логин администратора ИБ с прошлой попытки (null — первый показ).</param>
        public RacInfobaseDropRetryWindow(string errorText, string? login = null)
        {
            InitializeComponent();

            // Кнопка «развернуть» в собственной шапке не нужна (issue #3): ресайз
            // (ResizeMode по умолчанию CanResize) сохраняется, максимизация подавляется.
            WindowChromeHelper.SetSuppressMaximizeButton(this, true);

            ErrorText.Text = string.IsNullOrWhiteSpace(errorText) ? "—" : errorText;
            PromptText.Text = LocalizationManager.T("ServerMonitor.Infobase.Drop.RetryPrompt");
            LoginBox.Text = login ?? string.Empty;
            PasswordBox.Focus();
            Loaded += (_, _) => PasswordBox.Focus();

            // issue #69, комментарий 7OH от 2026-10-26: пользователь ввёл логин/пароль,
            // нажал «Повторить попытку» — и не знает, что делать дальше. По типовой
            // ошибке rac показываем подсказку и (для занятой базы) кнопку «Завершить
            // сеансы и повторить».
            var kind = ServerMonitorViewModel.ClassifyDropError(errorText);
            var hint = ServerMonitorViewModel.BuildDropRetryHint(kind);
            if (!string.IsNullOrEmpty(hint))
            {
                SuggestionText.Text = hint;
                SuggestionText.Visibility = Visibility.Visible;
            }
            if (kind == ServerMonitorViewModel.RacDropErrorKind.Sessions)
                TerminateSessionsButton.Visibility = Visibility.Visible;
        }

        private void OnTerminateSessions_Click(object sender, RoutedEventArgs e)
        {
            User = LoginBox.Text;
            Password = PasswordBox.Password;
            Retry = true;
            TerminateSessions = true;
            DialogResult = true;
            Close();
        }

        private void OnRetry_Click(object sender, RoutedEventArgs e)
        {
            User = LoginBox.Text;
            Password = PasswordBox.Password;
            Retry = true;
            DialogResult = true;
            Close();
        }

        private void OnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}