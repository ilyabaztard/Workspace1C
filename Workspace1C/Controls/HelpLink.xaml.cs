using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Workspace1C.Controls
{
    /// <summary>
    /// Гиперссылка в виде вопросительного знака «?». По клику открывает всплывающую
    /// подсказку с описанием поведения элемента интерфейса и способов взаимодействия с ним.
    /// Подсказка закрывается повторным кликом по «?» или кликом в любом другом месте окна.
    /// Свойство <see cref="HelpText"/> содержит текст справки (можно с переносами строк).
    /// </summary>
    public partial class HelpLink : UserControl
    {
        /// <summary>Текст всплывающей подсказки, описывающий поведение элемента и способы взаимодействия.</summary>
        public static readonly DependencyProperty HelpTextProperty =
            DependencyProperty.Register(
                nameof(HelpText),
                typeof(string),
                typeof(HelpLink),
                new PropertyMetadata(string.Empty));

        public string HelpText
        {
            get => (string)GetValue(HelpTextProperty);
            set => SetValue(HelpTextProperty, value);
        }

        /// <summary>
        /// Максимальная ширина всплывающей подсказки (по умолчанию 460). Для справок
        /// с длинными строками (например, ключи запуска 1С в «Текущая сессия», issue #45)
        /// задаётся шире, чтобы строка не переносилась посередине.
        /// </summary>
        public static readonly DependencyProperty PopupMaxWidthProperty =
            DependencyProperty.Register(
                nameof(PopupMaxWidth),
                typeof(double),
                typeof(HelpLink),
                new PropertyMetadata(460.0));

        public double PopupMaxWidth
        {
            get => (double)GetValue(PopupMaxWidthProperty);
            set => SetValue(PopupMaxWidthProperty, value);
        }

        private Window? _host;

        public HelpLink()
        {
            InitializeComponent();
        }

        private void OnHelpToggle_Checked(object sender, RoutedEventArgs e)
        {
            HelpPopup.IsOpen = true;
            _host = Window.GetWindow(this);
            if (_host != null)
            {
                _host.PreviewMouseDown += OnHostMouseDown;
                _host.Closed += OnHostClosed;
            }
        }

        private void OnHelpToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            HelpPopup.IsOpen = false;
            DetachHost();
        }

        private void OnHostClosed(object? sender, System.EventArgs e)
        {
            DetachHost();
            HelpToggle.IsChecked = false;
        }

        private void DetachHost()
        {
            if (_host != null)
            {
                _host.PreviewMouseDown -= OnHostMouseDown;
                _host.Closed -= OnHostClosed;
                _host = null;
            }
        }

        // Клик вне подсказки и вне самой кнопки «?» закрывает подсказку.
        private void OnHostMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is not DependencyObject source)
            {
                HelpToggle.IsChecked = false;
                return;
            }

            if (IsWithin(source, HelpToggle))
                return; // клик по самой кнопке — ею управляет ToggleButton

            HelpToggle.IsChecked = false;
        }

        private void OnHelpPopup_Closed(object? sender, System.EventArgs e)
        {
            // При любом закрытии попапа синхронизируем состояние кнопки.
            HelpToggle.IsChecked = false;
        }

        private static bool IsWithin(DependencyObject child, DependencyObject root)
        {
            DependencyObject? current = child;
            while (current != null)
            {
                if (ReferenceEquals(current, root))
                    return true;
                current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
            }
            return false;
        }
    }
}