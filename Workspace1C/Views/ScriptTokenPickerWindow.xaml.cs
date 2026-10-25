#if WINDOWS
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Workspace1C.Models;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Выбор переменной» подстановок сценариев (issue #67): список всех
    /// переменных базы («имя переменной — значение»), поиск, вставка токена по
    /// «Ок» и двойному клику. WPF-версия; Avalonia — <c>ScriptTokenPickerWindow.Avalonia.cs</c>.
    /// </summary>
    public partial class ScriptTokenPickerWindow : Window
    {
        private readonly ScriptTokenPickerViewModel _viewModel;

        /// <param name="infobase">База «для примера подстановок» (значения переменных).</param>
        public ScriptTokenPickerWindow(Infobase? infobase)
        {
            InitializeComponent();
            _viewModel = new ScriptTokenPickerViewModel(infobase);
            TokenList.ItemsSource = _viewModel.Items;
            TokenList.SelectedItem = _viewModel.SelectedItem;
        }

        /// <summary>Выбранный токен подстановки (при подтверждении), иначе null.</summary>
        public string? SelectedToken { get; private set; }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            _viewModel.Query = SearchBox.Text ?? string.Empty;
            // Предпочтение выделения списку (вставка токена по Enter не требуется).
        }

        private void OnTokenDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (TokenList.SelectedItem is ScriptTokenOption option)
            {
                ConfirmAndClose(option.Token);
            }
        }

        private void OnOk_Click(object sender, RoutedEventArgs e)
        {
            if (TokenList.SelectedItem is ScriptTokenOption option)
                ConfirmAndClose(option.Token);
            else if (_viewModel.Items.Count > 0)
                ConfirmAndClose(_viewModel.Items[0].Token);
        }

        private void OnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void ConfirmAndClose(string token)
        {
            SelectedToken = token;
            DialogResult = true;
        }
    }
}
#endif