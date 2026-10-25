using System.Windows;
using Workspace1C.Localization;

namespace Workspace1C
{
    /// <summary>
    /// Диалог повтора удаления информационной базы кластера (issue #69): при неудачном
    /// rac «infobase drop» (например, защищённая база) вместо простого окна с OK —
    /// текст ошибки, поля «Логин»/«Пароль» администратора ИБ и кнопки «Повторить
    /// попытку» (default) / «Отмена» (cancel). Логин сохраняется между показами
    /// диалога, пароль вводится заново при каждой попытке.
    /// </summary>
    public partial class RacInfobaseDropRetryWindow : Window
    {
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
            InitializeComponent();

            // Кнопка «развернуть» в собственной шапке не нужна (issue #3): ресайз
            // (ResizeMode по умолчанию CanResize) сохраняется, максимизация подавляется.
            WindowChromeHelper.SetSuppressMaximizeButton(this, true);

            ErrorText.Text = string.IsNullOrWhiteSpace(errorText) ? "—" : errorText;
            PromptText.Text = LocalizationManager.T("ServerMonitor.Infobase.Drop.RetryPrompt");
            LoginBox.Text = login ?? string.Empty;
            PasswordBox.Focus();
            Loaded += (_, _) => PasswordBox.Focus();
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