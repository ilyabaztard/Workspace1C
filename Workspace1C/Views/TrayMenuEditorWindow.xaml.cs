#if WINDOWS
using System;
using System.Windows;
using System.Windows.Controls;
using Workspace1C.Localization;
using Workspace1C.Services;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Редактор структуры меню системного трея (issue #62): дерево блоков верхнего
    /// уровня с перемещением, включением/отключением (видимость), сменой стиля
    /// блока («Блок меню»/«Подменю»), созданием пользовательских групп с иконками,
    /// разделителями, сбросом к стандартной компоновке и сохранением в файл
    /// <c>tray_menu_layout.json</c>. Фиксированные пункты меню трея
    /// («Открыть программу», «Настройки», «Выход») в дереве не отображаются —
    /// их всегда добавляют адаптеры трея.
    /// </summary>
    public partial class TrayMenuEditorWindow : Window
    {
        private readonly TrayMenuEditorViewModel _vm;

        /// <param name="store">Хранилище компоновки (или null — редактирование без сохранения).</param>
        public TrayMenuEditorWindow(ITrayMenuLayoutStore? store)
        {
            InitializeComponent();
            _vm = new TrayMenuEditorViewModel(store);
            DataContext = _vm;
            Owner = Application.Current?.MainWindow;
            Loaded += (_, _) => ExpandAll(TrayMenuTree.Items);
        }

        /// <summary>Компоновка после редактирования (актуальна после «Сохранить»).</summary>
        public Workspace1C.Models.TrayMenuLayout Layout => _vm.Layout;

        /// <summary>Синхронизирует выбранный узел дерева с моделью представления.</summary>
        private void OnTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            _vm.SelectedItem = e.NewValue as TrayMenuNodeItem;
        }

        /// <summary>«Добавить группу»: диалог имени/иконки → команда создания группы.</summary>
        private void OnAddGroupClick(object sender, RoutedEventArgs e)
        {
            var dialog = new UtilitiesGroupEditWindow(_vm.CatalogIcons) { Owner = this };
            if (dialog.ShowDialog() == true && dialog.Result is not null)
                _vm.AddGroupCommand.Execute(ToTrayGroupParams(dialog.Result));
        }

        /// <summary>«Переименовать»: диалог с текущим именем/иконкой → команда переименования
        /// пользовательской группы (issue #62). Для стандартных блоков кнопка неактивна.</summary>
        private void OnEditGroupClick(object sender, RoutedEventArgs e)
        {
            var item = _vm.SelectedItem;
            if (item is null || !_vm.CanEditGroup)
                return;
            var dialog = new UtilitiesGroupEditWindow(
                _vm.CatalogIcons,
                item.Title,
                item.Node.IconKey) { Owner = this };
            if (dialog.ShowDialog() == true && dialog.Result is not null)
                _vm.EditGroupCommand.Execute(ToTrayGroupParams(dialog.Result));
        }

        /// <summary>«Удалить»: удаляет пользовательскую группу или разделитель (без
        /// подтверждения — узлы верхнего уровня и легко добавляются заново; issue #62).</summary>
        private void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            _vm.DeleteCommand.Execute(null);
        }

        /// <summary>«Сохранить»: сохраняет компоновку и закрывает окно (как «Готово» других редакторов).</summary>
        private void OnOk_Click(object sender, RoutedEventArgs e)
        {
            _vm.SaveCommand.Execute(null);
            DialogResult = true;
        }

        private static TrayMenuGroupEditParams ToTrayGroupParams(UtilitiesGroupEditParams source) =>
            new(source.Title, source.IconKey);

        private void ExpandAll(ItemCollection items)
        {
            foreach (var obj in items)
            {
                if (obj is TreeViewItem item)
                {
                    item.IsExpanded = true;
                    ExpandAll(item.Items);
                }
            }
        }
    }
}
#endif