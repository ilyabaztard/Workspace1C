using System.Windows;
using Workspace1C.Localization;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Диалог повтора удаления информационной базы кластера (issue #69): при неудачном
    /// rac «infobase drop» (например, защищённая база) вместо простого окна с OK —
    /// текст ошибки, поля «Логин»/«Пароль» администратора ИБ и кнопки «Повторить
    /// попытку» (default) / «Отмена» (cancel). Логин и пароль сохраняются между
    /// показами диалога (комментарий 7OH от 2026-10-26). Типовая ошибка rac
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

        /// <summary>True — пользователь поставил галочку «Установить блокировку регламентных
        /// заданий» (issue #69, комментарий 7OH от 2026-10-26): перед завершением сеансов
        /// сначала выполняется rac «infobase update --scheduled-jobs-deny=on».</summary>
        public bool BlockScheduledJobs { get; private set; }

        /// <param name="errorText">Текст ошибки удаления («Не удалось удалить информационную
        /// базу.» + детали rac либо «Повторная попытка... не удалась: ...») — показывается
        /// в подписи окна.</param>
        /// <param name="login">Логин администратора ИБ с прошлой попытки (null — первый показ).</param>
        /// <param name="password">Пароль администратора ИБ с прошлой попытки (null — первый
        /// показ): сохраняется между попытками (комментарий 7OH от 2026-10-26).</param>
        public RacInfobaseDropRetryWindow(string errorText, string? login = null, string? password = null)
        {
            InitializeComponent();

            // Кнопка «развернуть» в собственной шапке не нужна (issue #3): ресайз
            // (ResizeMode по умолчанию CanResize) сохраняется, максимизация подавляется.
            WindowChromeHelper.SetSuppressMaximizeButton(this, true);

            ErrorText.Text = string.IsNullOrWhiteSpace(errorText) ? "—" : errorText;
            PromptText.Text = LocalizationManager.T("ServerMonitor.Infobase.Drop.RetryPrompt");
            LoginBox.Text = login ?? string.Empty;
            PasswordBox.Password = password ?? string.Empty;
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
            {
                TerminateSessionsButton.Visibility = Visibility.Visible;
                // issue #69, комментарий 7OH от 2026-10-26: вместе с кнопкой «Завершить
                // сеансы и повторить» предлагается галочка «Установить блокировку
                // регламентных заданий» — при повторном «Завершить сеансы и повторить»
                // сначала ставится блокировка, потом снимаются сеансы.
                BlockScheduledJobsCheckBox.Visibility = Visibility.Visible;
            }
        }

        private void OnTerminateSessions_Click(object sender, RoutedEventArgs e)
        {
            User = LoginBox.Text;
            Password = PasswordBox.Password;
            Retry = true;
            TerminateSessions = true;
            BlockScheduledJobs = BlockScheduledJobsCheckBox.IsChecked == true;
            DialogResult = true;
            Close();
        }

        private void OnRetry_Click(object sender, RoutedEventArgs e)
        {
            User = LoginBox.Text;
            Password = PasswordBox.Password;
            Retry = true;
            BlockScheduledJobs = BlockScheduledJobsCheckBox.IsChecked == true;
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