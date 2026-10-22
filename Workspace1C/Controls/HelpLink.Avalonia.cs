#if LINUX
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Workspace1C.Localization;
using Workspace1C.Themes;

namespace Workspace1C.Controls
{
    /// <summary>
    /// Avalonia-версия гиперссылки в виде вопросительного знака «?». По клику открывает
    /// всплывающую подсказку с описанием поведения элемента. Свойство <see cref="HelpText"/>
    /// содержит текст справки (можно с переносами строк). Закрывается повторным кликом
    /// или кликом вне подсказки.
    /// </summary>
    public class HelpLink : UserControl
    {
        /// <summary>Текст всплывающей подсказки.</summary>
        public static readonly StyledProperty<string> HelpTextProperty =
            AvaloniaProperty.Register<HelpLink, string>(nameof(HelpText), string.Empty);

        public string HelpText
        {
            get => GetValue(HelpTextProperty);
            set => SetValue(HelpTextProperty, value);
        }

        /// <summary>
        /// Максимальная ширина текста подсказки (по умолчанию 460). Для справок с длинными
        /// строками (ключи запуска 1С в «Текущая сессия», issue #45) задаётся шире.
        /// </summary>
        public static readonly StyledProperty<double> PopupMaxWidthProperty =
            AvaloniaProperty.Register<HelpLink, double>(nameof(PopupMaxWidth), 460);

        public double PopupMaxWidth
        {
            get => GetValue(PopupMaxWidthProperty);
            set => SetValue(PopupMaxWidthProperty, value);
        }

        private readonly ToggleButton _helpToggle = new();
        private readonly Popup _helpPopup = new();
        private readonly TextBlock _helpBody;

        public HelpLink()
        {
            // Круглая кнопка «?». Отступы снимаются и содержимое центрируется явно:
            // у кнопки по умолчанию Padding больше самой кнопки шириной 18, поэтому
            // знак вопроса вытеснялся за границы и кнопка выглядела пустым квадратом.
            _helpToggle.Width = 18;
            _helpToggle.Height = 18;
            _helpToggle.Padding = new Thickness(0);
            _helpToggle.CornerRadius = new CornerRadius(9);
            _helpToggle.HorizontalContentAlignment = HorizontalAlignment.Center;
            _helpToggle.VerticalContentAlignment = VerticalAlignment.Center;
            _helpToggle.HorizontalAlignment = HorizontalAlignment.Center;
            _helpToggle.VerticalAlignment = VerticalAlignment.Center;
            // Цвета как в разметке WPF: прозрачный фон, обводка и знак акцентным
            // цветом. Раньше кнопка брала цвета темы и терялась среди команд.
            var questionMark = new TextBlock
            {
                Text = "?",
                FontWeight = FontWeight.Bold,
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            ThemeBrushes.Bind(questionMark, TextBlock.ForegroundProperty, "AccentColorBrush");
            _helpToggle.Content = questionMark;
            _helpToggle.Background = Brushes.Transparent;
            _helpToggle.BorderThickness = new Thickness(1);
            ThemeBrushes.Bind(_helpToggle, TemplatedControl.BorderBrushProperty, "AccentColorBrush");
            ToolTip.SetTip(_helpToggle, LocalizationManager.T("HelpLink.Tooltip"));
            _helpToggle.IsCheckedChanged += (_, _) => OnToggleChanged();

            // Всплывающая подсказка.
            _helpBody = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 460,
                Margin = new Thickness(0, 6, 0, 0)
            };
            var titleBlock = new TextBlock
            {
                Text = LocalizationManager.T("HelpLink.Title"),
                FontWeight = FontWeight.SemiBold,
                FontSize = 12
            };
            // Заголовок — акцентный цвет темы.
            ThemeBrushes.Bind(titleBlock, TextBlock.ForegroundProperty, "AccentColorBrush");
            var popupContent = new StackPanel { Children = { titleBlock, _helpBody } };
            var popupBorder = new Border
            {
                Child = popupContent,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 6, 0, 0)
            };
            // Фон и граница подсказки — карточка и граница из темы.
            ThemeBrushes.Bind(popupBorder, Border.BackgroundProperty, "CardBackgroundColorBrush");
            ThemeBrushes.Bind(popupBorder, Border.BorderBrushProperty, "BorderColorBrush");
            _helpPopup.Child = popupBorder;
            _helpPopup.PlacementTarget = _helpToggle;
            _helpPopup.Placement = PlacementMode.Bottom;
            _helpPopup.IsLightDismissEnabled = true;
            _helpPopup.Closed += (_, _) => _helpToggle.IsChecked = false;

            Content = new Grid { Children = { _helpToggle, _helpPopup } };
        }

        private void OnToggleChanged()
        {
            if (_helpToggle.IsChecked == true)
            {
                _helpBody.Text = HelpText;
                _helpBody.MaxWidth = PopupMaxWidth;
                _helpPopup.IsOpen = true;
            }
            else
            {
                _helpPopup.IsOpen = false;
            }
        }
    }
}
#endif