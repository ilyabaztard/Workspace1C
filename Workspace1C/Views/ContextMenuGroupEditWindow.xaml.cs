#if WINDOWS
using System.Windows;
using System.Windows.Controls;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Диалог создания/правки группы контекстного меню (issue #56): имя группы.
    /// (Иконки пунктов контекстного меню фиксированы каталогом, поэтому блок
    /// «Иконки» в диалоге отсутствует.) Результат — <see cref="ContextMenuGroupEditParams"/>
    /// через <see cref="Result"/> (null — отмена).
    /// </summary>
    public partial class ContextMenuGroupEditWindow : Window
    {
        /// <param name="initialTitle">Текущее имя группы (для правки) или пусто.</param>
        public ContextMenuGroupEditWindow(string? initialTitle = null)
        {
            InitializeComponent();
            Owner = Application.Current?.MainWindow;
            NameBox.Text = initialTitle ?? string.Empty;
            OkButton.IsEnabled = !string.IsNullOrWhiteSpace(NameBox.Text);

            Loaded += (_, _) =>
            {
                // Активное поле «Имя» с выделением текста (как в UtilitiesGroupEditWindow).
                // Фокус откладываем: в Loaded окно ещё не активировано, и Focus() не встаёт.
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    Activate();
                    NameBox.Focus();
                    NameBox.SelectAll();
                }), System.Windows.Threading.DispatcherPriority.Input);
                // Диалог чуть выше центра владельца (как в UtilitiesGroupEditWindow).
                Top = Math.Max(SystemParameters.WorkArea.Top, Top - 120);
            };
        }

        /// <summary>Результат диалога: имя группы (null — отмена).</summary>
        public ContextMenuGroupEditParams? Result { get; private set; }

        private void OnNameBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            OkButton.IsEnabled = !string.IsNullOrWhiteSpace(NameBox.Text);
        }

        private void OnOk_Click(object sender, RoutedEventArgs e)
        {
            Result = new ContextMenuGroupEditParams(NameBox.Text?.Trim() ?? string.Empty);
            DialogResult = true;
        }
    }
}
#endif