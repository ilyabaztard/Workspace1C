#if WINDOWS
using System.Windows;
using System.Windows.Controls;
using Workspace1C.Localization;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    public partial class SettingsWindow
    {
        /// <summary>
        /// Строит вкладку «Учётные записи» (управление профилями) в окне настроек вместо
        /// отдельного окна. Вся бизнес-логика вынесена в <see cref="ProfilesViewModel"/>,
        /// разметка — в <see cref="ProfilesPanel"/>. Вкладка вставляется перед последней
        /// вкладкой («О программе»), как и вкладка «Резервное копирование».
        /// </summary>
        private void InitializeAccountsTab()
        {
            var tab = new TabItem();
            try { tab.Style = (Style)FindResource("SettingsTabItem"); } catch { /* стандартный вид */ }

            var tabIcon = new MaterialDesignThemes.Wpf.PackIcon
            {
                Kind = MaterialDesignThemes.Wpf.PackIconKind.AccountMultiple,
                Width = 18,
                Height = 18,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            tabIcon.SetBinding(MaterialDesignThemes.Wpf.PackIcon.ForegroundProperty,
                new System.Windows.Data.Binding("Foreground")
                {
                    RelativeSource = new System.Windows.Data.RelativeSource(
                        System.Windows.Data.RelativeSourceMode.FindAncestor,
                        typeof(System.Windows.Controls.TabItem), 1)
                });
            var tabHeader = new StackPanel { Orientation = Orientation.Horizontal };
            tabHeader.Children.Add(tabIcon);
            var title = new TextBlock
            {
                Text = LocalizationManager.T("Settings.TabAccounts"),
                VerticalAlignment = VerticalAlignment.Center
            };
            // Цвет текста заголовка привязан к Foreground вкладки так же, как в
            // статичной разметке вкладок (SettingsWindow.xaml:296): при выборе
            // пункта тема красит подпись контрастной кистью TreeSelectedTextBrush
            // (issue #26), а не наследуемым значением — WPF не передаёт его от
            // шаблона в TextBlock через attached TextElement.Foreground.
            title.SetBinding(TextBlock.ForegroundProperty,
                new System.Windows.Data.Binding("Foreground")
                {
                    RelativeSource = new System.Windows.Data.RelativeSource(
                        System.Windows.Data.RelativeSourceMode.FindAncestor,
                        typeof(System.Windows.Controls.TabItem), 1)
                });
            tabHeader.Children.Add(title);
            tab.Header = tabHeader;

            // Свежий экземпляр ViewModel: конструктор сам загружает список учётных записей.
            var viewModel = AppServices.GetRequiredService<ProfilesViewModel>();
            var panel = new ProfilesPanel
            {
                Margin = new Thickness(4, 12, 4, 0),
                DataContext = viewModel
            };

            tab.Content = new ScrollViewer
            {
                Content = panel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            // Вставляем перед последней вкладкой («О программе»), чтобы она шла выше.
            SettingsTabs.Items.Insert(Math.Max(0, SettingsTabs.Items.Count - 1), tab);
        }
    }
}
#endif