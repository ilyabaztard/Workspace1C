#if LINUX
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Workspace1C.Localization;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Сменить платформу» (issue #54, переработка по комментарию 7OH, Avalonia/Linux):
    /// выбор версии платформы 1С из установленных и замена её у ВЫДЕЛЕННЫХ баз по «Сменить».
    /// Вся логика — в чистой <see cref="PlatformChangeViewModel"/> (применение через
    /// <c>ConnectionReplacementPlanner.ApplyPlatform</c>, undo через
    /// <see cref="MainViewModel.ApplyConnectionReplace"/>); окно — тонкая обёртка (аналог
    /// <see cref="ConnectionReplaceWindow"/>): ComboBox версий, сводка и кнопки
    /// «Сменить»/«Отмена». По «Сменить» — команда VM и закрытие с DialogResult=true.
    /// </summary>
    public sealed class PlatformChangeWindow : ModalWindowBase
    {
        private readonly PlatformChangeViewModel _vm;

        /// <param name="vm">Готовый ViewModel окна (кандидаты и колбэк формирует MainViewModel).</param>
        public PlatformChangeWindow(PlatformChangeViewModel vm)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));

            Title = LocalizationManager.T("PlatformChange.Title");
            Width = 460;
            CanResize = false;
            FontSize = 13;
            DataContext = _vm;

            // ---- Выбор версии платформы. ----
            var platformCombo = new ComboBox
            {
                Width = 260,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            platformCombo.Styled(ControlThemes.ModernComboBox);
            platformCombo.ItemsSource = _vm.InstalledPlatformVersions;
            platformCombo.Bind(ComboBox.SelectedItemProperty,
                new Binding("SelectedPlatform", BindingMode.TwoWay));

            // ---- Сводка и результат. ----
            var summary = new TextBlock
            {
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 12, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            summary.Bind(TextBlock.TextProperty, new Binding("SummaryText"));
            var result = new TextBlock
            {
                FontSize = 12,
                Opacity = 0.85,
                Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            result.Bind(TextBlock.TextProperty, new Binding("ResultText"));

            // ---- Кнопки: Сменить / Отмена. ----
            var applyButton = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new TextBlock { Text = "✓", VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White },
                        new TextBlock { Text = LocalizationManager.T("PlatformChange.ApplyButton"), VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White }
                    }
                },
                Padding = new Thickness(16, 6)
            };
            applyButton.Styled(ControlThemes.ModernButton);
            applyButton.Background = new SolidColorBrush(Color.Parse("#22C55E"));
            applyButton.Foreground = Brushes.White;
            applyButton.Bind(Button.IsEnabledProperty, new Binding("CanApply"));
            applyButton.Click += (_, _) => OnApplyClick();

            var cancelButton = new Button
            {
                Content = LocalizationManager.T("Common.Cancel"),
                Padding = new Thickness(16, 6)
            };
            cancelButton.Styled(ControlThemes.ModernButton);
            ThemeBrushes.Bind(cancelButton, Button.BackgroundProperty, "ItemHoverBrush");
            cancelButton.Click += (_, _) =>
            {
                DialogResult = false;
                Close();
            };

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 20, 0, 0),
                Children = { applyButton, cancelButton }
            };

            var panel = new StackPanel
            {
                Margin = new Thickness(20, 16),
                Children =
                {
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 6,
                        Children =
                        {
                            FieldLabel("PlatformChange.PlatformLabel"),
                            platformCombo
                        }
                    },
                    summary,
                    result,
                    buttons
                }
            };
            Content = panel;
        }

        private void OnApplyClick()
        {
            _vm.ApplyCommand.Execute(null);
            DialogResult = true;
            Close();
        }

        /// <summary>Подпись поля формы по ключу локализации (issue #61): единая кисть
        /// FormLabelBrush — читаема в обеих темах и пользовательских схемах.</summary>
        private static TextBlock FieldLabel(string textKey)
        {
            var block = new TextBlock
            {
                Text = LocalizationManager.T(textKey),
                VerticalAlignment = VerticalAlignment.Center
            };
            ThemeBrushes.Bind(block, TextBlock.ForegroundProperty, "FormLabelBrush");
            return block;
        }
    }
}
#endif