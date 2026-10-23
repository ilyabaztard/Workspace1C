#if WINDOWS
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Workspace1C.Localization;
using Workspace1C.Services;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Заменить в строках подключения…» (0.3.9.190, Windows/WPF): массовая замена
    /// в строке подключения баз по правилу «найти → заменить на». Вся логика — в чистой
    /// <see cref="ConnectionReplaceViewModel"/>; окно — тонкая обёртка (как
    /// <see cref="ClusterImportWindow"/>): конструктор принимает готовый VM, поля и
    /// ComboBox'ы привязаны к нему (списки <c>Fields</c>/<c>Scopes</c>/<c>Modes</c> с
    /// локализованными текстами), предпросмотр «База | Поле | Было | Станет» с подсветкой
    /// изменённых строк (колонка «Станет» — зелёный фон при <c>Changed</c>). Подтверждение
    /// перед применением — через <see cref="IDialogService.Confirm"/> с ключом
    /// <c>ConnectionReplace.Confirm.ApplyFormat</c> (число баз = AffectedCount): при отказе
    /// пользователя базы не мутируются. После успешного применения окно НЕ закрывается —
    /// показывает результат и активную кнопку «Отменить последнюю замену»; закрытие
    /// (Esc/«Закрыть») ничего не меняет (Plan не мутирует базы).
    /// </summary>
    public partial class ConnectionReplaceWindow : Window
    {
        private readonly ConnectionReplaceViewModel _vm;
        private readonly IDialogService _dialogs;

        /// <param name="vm">Готовый ViewModel окна (кандидаты области и колбэки формирует MainViewModel).</param>
        public ConnectionReplaceWindow(ConnectionReplaceViewModel vm)
        {
            InitializeComponent();

            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            _dialogs = AppServices.GetRequiredService<IDialogService>();

            DataContext = _vm;
            Title = LocalizationManager.T("ConnectionReplace.Title");
            PreviewGrid.ItemsSource = _vm.PreviewRows;
        }

        /// <summary>
        /// «Заменить»: подтверждение с числом затронутых баз через IDialogService; при отказе
        /// пользователя применение не выполняется. После применения окно остаётся открытым
        /// (VM выводит ResultText и разблокирует «Отменить последнюю замену»).
        /// </summary>
        private void OnApply_Click(object sender, RoutedEventArgs e)
        {
            if (!_vm.CanApply || _vm.IsPreviewDirty)
                return;

            var message = string.Format(
                LocalizationManager.T("ConnectionReplace.Confirm.ApplyFormat"),
                _vm.AffectedCount);
            if (!_dialogs.Confirm(message, Title))
                return;

            _vm.ApplyCommand.Execute(null);
        }

        private void OnClose_Click(object sender, RoutedEventArgs e) => Close();
    }

    /// <summary>
    /// Конвертер локализованного имени поля в колонке «Поле» предпросмотра:
    /// имя поля строки подключения (<c>ConnectionReplace.Fields.*</c>; единая точка —
    /// метод <see cref="ConnectionReplaceViewModel.FieldDisplayText"/>). Принимает строку
    /// предпросмотра целиком (в XAML — привязка без Path).
    /// </summary>
    public sealed class ConnectionFieldToTextConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is ConnectionReplacePreviewRow row
                ? ConnectionReplaceViewModel.FieldDisplayText(row.Field)
                : string.Empty;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
#endif