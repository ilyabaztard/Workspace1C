#if WINDOWS
using System.Windows;
using Workspace1C.Localization;

namespace Workspace1C
{
    /// <summary>
    /// Диалог правки пользовательского параметра справочника окна «Запустить с выбором
    /// параметров» (issue #68): ключ командной строки и описание. Работает на копии
    /// данных; результат возвращается через <see cref="Key"/>/<see cref="Comment"/>
    /// при подтверждении.
    /// </summary>
    public partial class LaunchParameterEditWindow : Window
    {
        /// <param name="key">Текущий ключ параметра.</param>
        /// <param name="comment">Текущее описание параметра.</param>
        public LaunchParameterEditWindow(string? key, string? comment)
        {
            InitializeComponent();
            KeyBox.Text = key ?? string.Empty;
            CommentBox.Text = comment ?? string.Empty;
            KeyBox.Focus();
            KeyBox.SelectAll();
            UpdateSaveState();
        }

        /// <summary>Новое значение ключа (после подтверждения).</summary>
        public string? Key => KeyBox.Text;

        /// <summary>Новое значение описания (после подтверждения).</summary>
        public string? Comment => CommentBox.Text;

        private void OnTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => UpdateSaveState();

        private void UpdateSaveState()
        {
            SaveButton.IsEnabled = !string.IsNullOrWhiteSpace(KeyBox.Text);
        }

        private void OnSave_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(KeyBox.Text))
            {
                ErrorText.Text = LocalizationManager.T("LaunchParams.KeyRequired");
                ErrorText.Visibility = Visibility.Visible;
                KeyBox.Focus();
                return;
            }

            DialogResult = true;
        }

        private void OnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
#endif