#if LINUX
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Workspace1C.Models;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Выбор переменной» подстановок сценариев (issue #67): список всех
    /// переменных базы («имя переменной — значение»), поиск, вставка токена по
    /// «Ок» и двойному клику. Avalonia/Linux-версия WPF-окна
    /// <c>ScriptTokenPickerWindow</c>.
    /// </summary>
    public sealed class ScriptTokenPickerWindow : ModalWindowBase
    {
        private readonly ScriptTokenPickerViewModel _viewModel;
        private readonly TextBox _searchBox;
        private readonly ListBox _list;

        /// <param name="infobase">База «для примера подстановок» (значения переменных).</param>
        public ScriptTokenPickerWindow(Infobase? infobase)
        {
            _viewModel = new ScriptTokenPickerViewModel(infobase);

            Title = Workspace1C.Localization.LocalizationManager.T("Script.TokenPickerTitle");
            FontSize = 13;
            Width = 760;
            Height = 520;
            MinWidth = 620;
            MinHeight = 400;

            _searchBox = new TextBox
            {
                MinHeight = 30,
                Padding = new Thickness(6, 3),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            _searchBox.Styled(ControlThemes.ModernTextBox);
            ToolTip.SetTip(_searchBox, Workspace1C.Localization.LocalizationManager.T("Script.TokenSearch"));
            _searchBox.TextChanged += (_, _) => _viewModel.Query = _searchBox.Text ?? string.Empty;

            _list = new ListBox { ItemsSource = _viewModel.Items };
            ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
            _list.ItemTemplate = new FuncDataTemplate<ScriptTokenOption>((item, _) =>
            {
                if (item is null)
                    return new Control();

                // Заголовок секции (Основные/Подключение/…): виден только у первой строки
                // секции (SectionStart=true), issue #67 — группировка полей структуры базы.
                var root = new StackPanel { Margin = new Thickness(2, 3) };
                if (item.SectionStart && !string.IsNullOrEmpty(item.SectionDisplay))
                {
                    var header = new TextBlock
                    {
                        Text = item.SectionDisplay,
                        FontSize = 12,
                        FontWeight = FontWeight.SemiBold,
                        Margin = new Thickness(0, 6, 0, 2)
                    };
                    Themes.ThemeBrushes.Bind(header, TextBlock.ForegroundProperty, "TextSecondaryBrush");
                    root.Children.Add(header);
                }

                var panel = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                panel.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(240)));
                panel.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

                var token = new TextBlock
                {
                    Text = item.Token,
                    FontWeight = FontWeight.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 12, 0)
                };
                Grid.SetColumn(token, 0);
                panel.Children.Add(token);

                var value = new TextBlock
                {
                    Text = item.DisplayValue,
                    FontSize = 12,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Themes.ThemeBrushes.Bind(value, TextBlock.ForegroundProperty, "TextSecondaryBrush");
                Grid.SetColumn(value, 1);
                panel.Children.Add(value);
                root.Children.Add(panel);

                // Пояснение значения (issue #67): «что означает и откуда берётся» — текст
                // всегда виден под строкой (поиск ищет и по нему; пароль не светится).
                var description = new TextBlock
                {
                    Text = item.DisplayDescription,
                    FontSize = 11,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 600,
                    Margin = new Thickness(14, 0, 2, 2),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Themes.ThemeBrushes.Bind(description, TextBlock.ForegroundProperty, "TextSecondaryBrush");
                root.Children.Add(description);

                return root;
            });
            _list.DoubleTapped += (_, e) =>
            {
                if (_list.SelectedItem is ScriptTokenOption option)
                {
                    ConfirmAndClose(option.Token);
                    e.Handled = true;
                }
            };

            Content = BuildRoot();
            Opened += (_, _) => _searchBox.Focus();
        }

        /// <summary>Выбранный токен подстановки (при подтверждении), иначе null.</summary>
        public string? SelectedToken { get; private set; }

        /// <summary>Показывает окно модально (синхронно).</summary>
        public bool ShowSync(Window? owner = null) => ShowDialogSync(owner);

        private Control BuildRoot()
        {
            var grid = new Grid { Margin = new Thickness(14) };
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            Grid.SetRow(_searchBox, 0);
            _searchBox.Margin = new Thickness(0, 0, 0, 8);
            grid.Children.Add(_searchBox);

            var listHost = new ScrollViewer
            {
                Content = _list,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            Grid.SetRow(listHost, 1);
            grid.Children.Add(listHost);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 10,
                Margin = new Thickness(0, 12, 0, 0),
                Children =
                {
                    BuildConfirmActionButton("Common.Ok", "IconCheck", 130, OnOkClick),
                    BuildCancelActionButton(130)
                }
            };
            Grid.SetRow(buttons, 2);
            grid.Children.Add(buttons);

            return grid;
        }

        private void OnOkClick()
        {
            if (_list.SelectedItem is ScriptTokenOption option)
                ConfirmAndClose(option.Token);
            else if (_viewModel.Items.Count > 0)
                ConfirmAndClose(_viewModel.Items[0].Token);
        }

        private void ConfirmAndClose(string token)
        {
            SelectedToken = token;
            DialogResult = true;
        }
    }
}
#endif