#if WINDOWS
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Диалог «В группу» редактора меню трея (issue #62): выбор пользовательской
    /// группы (подменю), куда перемещается выбранный блок/разделитель.
    /// Результат — <see cref="SelectedGroup"/> при подтверждении.
    /// </summary>
    public partial class TrayMenuGroupPickWindow : Window
    {
        /// <param name="groups">Список пользовательских групп-приёмников.</param>
        public TrayMenuGroupPickWindow(IReadOnlyList<TrayMenuNodeItem>? groups)
        {
            InitializeComponent();
            GroupList.ItemsSource = groups ?? (IReadOnlyList<TrayMenuNodeItem>)new List<TrayMenuNodeItem>();
            if (GroupList.Items.Count > 0)
                GroupList.SelectedIndex = 0;
            else
                NoGroupsHint.Visibility = Visibility.Visible;
        }

        /// <summary>Выбранная группа (при подтверждении), иначе null.</summary>
        public TrayMenuNodeItem? SelectedGroup { get; private set; }

        private void OnGroupDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (GroupList.SelectedItem is TrayMenuNodeItem group)
                ConfirmAndClose(group);
        }

        private void OnOk_Click(object sender, RoutedEventArgs e)
        {
            if (GroupList.SelectedItem is TrayMenuNodeItem group)
                ConfirmAndClose(group);
            else if (GroupList.Items.Count > 0 && GroupList.Items[0] is TrayMenuNodeItem first)
                ConfirmAndClose(first);
        }

        private void OnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void ConfirmAndClose(TrayMenuNodeItem group)
        {
            SelectedGroup = group;
            DialogResult = true;
        }
    }
}
#endif