#if LINUX
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Workspace1C.Localization;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Диалог создания/правки группы контекстного меню (issue #56): имя группы.
    /// Avalonia/Linux-версия WPF-окна <see cref="ContextMenuGroupEditWindow"/>.
    /// Результат — <see cref="ContextMenuGroupEditParams"/> через <see cref="Result"/>
    /// (null — отмена).
    /// </summary>
    public sealed class ContextMenuGroupEditWindow : ModalWindowBase
    {
        private readonly TextBox _nameBox = new();
        private readonly Button _okButton = new();

        /// <param name="initialTitle">Текущее имя группы (для правки) или пусто.</param>
        public ContextMenuGroupEditWindow(string? initialTitle = null)
        {
            Title = LocalizationManager.T("ContextMenu.Editor.GroupDialogTitle");
            Width = 440;
            // Минимальные размеры (issue #55): при уменьшении окна поля не обрезаются.
            MinWidth = 400;
            MinHeight = 200;
            // Высота по содержимому: фиксированная обрезала нижние кнопки (как в UtilitiesGroupEditWindow).
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            _nameBox.Text = initialTitle ?? string.Empty;
            _nameBox.TextChanged += (_, _) => UpdateOkEnabled();

            Content = BuildLayout();
            UpdateOkEnabled();
            Opened += (_, _) =>
            {
                // Активное поле «Имя» с выделением текста (как в UtilitiesGroupEditWindow).
                // Фокус откладываем: в Opened окно может быть ещё не активировано.
                Dispatcher.UIThread.Post(() =>
                {
                    Activate();
                    _nameBox.Focus();
                    _nameBox.SelectAll();
                });
                // Диалог чуть выше центра владельца (как в UtilitiesGroupEditWindow).
                Position = new PixelPoint(Position.X, Math.Max(0, Position.Y - 120));
            };
        }

        /// <summary>Результат диалога: имя группы (null — отмена).</summary>
        public ContextMenuGroupEditParams? Result { get; private set; }

        /// <summary>Показывает окно модально (синхронно). Открытая публичная обёртка над
        /// <see cref="ModalWindowBase.ShowDialogSync(Window?)"/>.</summary>
        public bool ShowSync(Window? owner = null) => ShowDialogSync(owner);

        private Control BuildLayout()
        {
            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            // Имя группы.
            var nameLabel = new TextBlock
            {
                Text = LocalizationManager.T("ContextMenu.Editor.GroupNameLabel"),
                Margin = new Thickness(0, 0, 0, 6)
            };
            Grid.SetRow(nameLabel, 0);
            grid.Children.Add(nameLabel);

            _nameBox.Padding = new Thickness(8, 6);
            Grid.SetRow(_nameBox, 1);
            grid.Children.Add(_nameBox);

            // Кнопки.
            var buttons = BuildButtons(
                okText: LocalizationManager.T("Common.Ok"), onOk: OnOkClick);
            Grid.SetRow(buttons, 2);
            buttons.Margin = new Thickness(0, 16, 0, 0);
            grid.Children.Add(buttons);

            return grid;
        }

        private void UpdateOkEnabled()
        {
            _okButton.IsEnabled = !string.IsNullOrWhiteSpace(_nameBox.Text);
        }

        private void OnOkClick()
        {
            Result = new ContextMenuGroupEditParams(_nameBox.Text?.Trim() ?? string.Empty);
            DialogResult = true;
            Close();
        }
    }
}
#endif