#if LINUX
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Workspace1C.Localization;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Диалог «В группу» редактора меню трея (issue #62): выбор пользовательской
    /// группы (подменю), куда перемещается выбранный блок/разделитель.
    /// Avalonia/Linux-версия WPF-окна <c>TrayMenuGroupPickWindow</c>.
    /// </summary>
    public sealed class TrayMenuGroupPickWindow : ModalWindowBase
    {
        private readonly ListBox _list;

        /// <param name="groups">Список пользовательских групп-приёмников.</param>
        public TrayMenuGroupPickWindow(IReadOnlyList<TrayMenuNodeItem>? groups)
        {
            Title = LocalizationManager.T("TrayMenu.PickGroupTitle");
            FontSize = 13;
            Width = 420;
            Height = 330;
            MinWidth = 360;
            MinHeight = 280;

            _list = new ListBox
            {
                ItemsSource = groups ?? (IReadOnlyList<TrayMenuNodeItem>)new List<TrayMenuNodeItem>()
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
            _list.ItemTemplate = new FuncDataTemplate<TrayMenuNodeItem>((item, _) =>
                item is null ? new Control() : new TextBlock { Text = item.Title, VerticalAlignment = VerticalAlignment.Center });
            _list.DoubleTapped += (_, e) =>
            {
                if (_list.SelectedItem is TrayMenuNodeItem group)
                {
                    ConfirmAndClose(group);
                    e.Handled = true;
                }
            };
            if (_list.Items.Count > 0)
                _list.SelectedIndex = 0;

            Content = BuildRoot();
        }

        /// <summary>Выбранная группа (при подтверждении), иначе null.</summary>
        public TrayMenuNodeItem? SelectedGroup { get; private set; }

        /// <summary>Показывает окно модально (синхронно).</summary>
        public bool ShowSync(Window? owner = null) => ShowDialogSync(owner);

        private Control BuildRoot()
        {
            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            Grid.SetRow(_list, 0);
            grid.Children.Add(_list);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 10,
                Margin = new Thickness(0, 12, 0, 0),
                Children =
                {
                    BuildConfirmActionButton("Common.Ok", "IconCheck", 120, OnOkClick),
                    BuildCancelActionButton(120)
                }
            };
            Grid.SetRow(buttons, 1);
            grid.Children.Add(buttons);
            return grid;
        }

        private void OnOkClick()
        {
            if (_list.SelectedItem is TrayMenuNodeItem group)
                ConfirmAndClose(group);
            else if (_list.Items.Count > 0 && _list.Items[0] is TrayMenuNodeItem first)
                ConfirmAndClose(first);
        }

        private void ConfirmAndClose(TrayMenuNodeItem group)
        {
            SelectedGroup = group;
            DialogResult = true;
        }
    }
}
#endif