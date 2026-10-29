#if LINUX
using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Workspace1C.Localization;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Командная палитра (Ctrl+K), Avalonia/Linux-версия WPF-окна
    /// <see cref="CommandPaletteWindow"/>. Раскладка лежит в CommandPaletteWindow.axaml,
    /// здесь только поведение: фильтрация по тексту, навигация стрелками,
    /// Enter — выполнить («1С:Предприятие» для базы), Shift+Enter — Конфигуратор, Esc — закрыть.
    /// </summary>
    public partial class CommandPaletteWindow : ModalWindowBase
    {
        private readonly CommandPaletteViewModel _palette = new();
        private TextBox _searchBox = null!;
        private ListBox _itemList = null!;

        /// <summary>Выбранный элемент (null — окно закрыто без выбора).</summary>
        public CommandPaletteItem? Result { get; private set; }

        /// <summary>True — выбор сделан с Shift (Конфигуратор для базы).</summary>
        public bool UseConfigurator { get; private set; }

        public CommandPaletteWindow(IEnumerable<CommandPaletteItem> bases, IEnumerable<CommandPaletteItem> commands)
        {
            AvaloniaXamlLoader.Load(this);

            Title = LocalizationManager.T("Palette.Title");
            this.FindControl<TextBlock>("HintText")!.Text = LocalizationManager.T("Palette.Hint");

            _searchBox = this.FindControl<TextBox>("SearchBox")!;
            _itemList = this.FindControl<ListBox>("ItemList")!;

            _palette.SetSource(bases, commands);
            _itemList.ItemsSource = _palette.VisibleItems;

            _searchBox.TextChanged += (_, _) =>
            {
                _palette.ApplyQuery(_searchBox.Text ?? "");
                _itemList.ItemsSource = _palette.VisibleItems;
                if (_palette.VisibleItems.Count > 0)
                    _itemList.SelectedIndex = _palette.SelectedIndex;
            };
            // Issue #84: стрелки Up/Down в TextBox помечаются Handled внутренней
            // обработкой Avalonia раньше, чем доходит обычный KeyDown (bubbling), —
            // подписываемся на KeyDownEvent с RoutingStrategies.Tunnel (аналог
            // PreviewKeyDown в WPF): обработчик приходит ДО внутренней обработки поля.
            _searchBox.AddHandler(InputElement.KeyDownEvent, OnSearchBox_PreviewKeyDown, RoutingStrategies.Tunnel);
            _itemList.SelectionChanged += (_, _) =>
            {
                if (_itemList.SelectedIndex >= 0)
                    _palette.SelectedIndex = _itemList.SelectedIndex;
            };
            _itemList.DoubleTapped += (_, _) => Confirm(useConfigurator: false);

            Opened += (_, _) =>
            {
                // Фокус ставим отложенно (после полного показа и активации окна):
                // синхронная установка в Opened слетает до отрисовки модального диалога.
                Dispatcher.UIThread.Post(() => _searchBox.Focus());
            };
        }

        /// <summary>
        /// Обработчик клавиш поисковой строки (issue #84). Подписка — туннелирующая
        /// (RoutingStrategies.Tunnel), чтобы стрелки Up/Down доходили до обработчика
        /// ДО внутренней обработки TextBox. Навигация — общая с WPF:
        /// <see cref="CommandPaletteViewModel.MoveSelection"/>; фокус остаётся в
        /// _searchBox, выбор списка синхронизируется по индексу.
        /// </summary>
        private void OnSearchBox_PreviewKeyDown(object? sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Down:
                    if (_palette.MoveSelection(+1))
                    {
                        _itemList.SelectedIndex = _palette.SelectedIndex;
                        _itemList.ScrollIntoView(_itemList.SelectedItem);
                    }
                    e.Handled = true;
                    break;

                case Key.Up:
                    if (_palette.MoveSelection(-1))
                    {
                        _itemList.SelectedIndex = _palette.SelectedIndex;
                        _itemList.ScrollIntoView(_itemList.SelectedItem);
                    }
                    e.Handled = true;
                    break;

                case Key.Enter:
                    Confirm(e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                    e.Handled = true;
                    break;

                case Key.Escape:
                    Result = null;
                    Close();
                    e.Handled = true;
                    break;
            }
        }

        private void Confirm(bool useConfigurator)
        {
            var current = _palette.Current;
            if (current is null)
            {
                Result = null;
            }
            else
            {
                Result = current;
                UseConfigurator = useConfigurator;
            }
            DialogResult = true;
            Close();
        }
    }
}
#endif
