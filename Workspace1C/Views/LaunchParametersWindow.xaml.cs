#if WINDOWS
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C
{
    /// <summary>
    /// Диалог-конфигуратор параметров запуска платформы 1С.
    /// Состоит из поля ввода параметров и справочника ключей командной строки,
    /// из которого параметр подставляется в поле двойным кликом.
    /// </summary>
    public partial class LaunchParametersWindow : Window
    {
        /// <summary>Пользовательские параметры, добавленные в справочник (issue #141).</summary>
        private readonly List<string> _customParams;

        /// <summary>Обратный вызов для сохранения списка пользовательских параметров при изменении.</summary>
        private readonly Action<IReadOnlyList<string>>? _onCustomParametersChanged;

        /// <summary>
        /// Создаёт диалог конфигуратора параметров запуска.
        /// </summary>
        /// <param name="currentParameters">Текущая строка параметров для предзаполнения.</param>
        /// <param name="customParameters">Пользовательские параметры, дополняющие справочник (необязательно).</param>
        /// <param name="onCustomParametersChanged">Обратный вызов сохранения изменённого списка пользовательских параметров (необязательно).</param>
        public LaunchParametersWindow(
            string currentParameters,
            IReadOnlyList<string>? customParameters = null,
            Action<IReadOnlyList<string>>? onCustomParametersChanged = null)
        {
            InitializeComponent();
            _onCustomParametersChanged = onCustomParametersChanged;
            _customParams = new List<string>();
            if (customParameters != null)
                _customParams.AddRange(customParameters.Where(p => !string.IsNullOrWhiteSpace(p)));
            TxtCustom.Text = currentParameters ?? string.Empty;
            LstReference.SelectionChanged += (_, _) => UpdateCustomButtonsState();
            RefreshReference();
        }

        /// <summary>
        /// Итоговая строка параметров запуска.
        /// </summary>
        public string Result { get; private set; } = string.Empty;

        /// <summary>Возвращает обновлённый список пользовательских параметров справочника (issue #141).</summary>
        public IReadOnlyList<string> CustomParameters => _customParams;

        /// <summary>
        /// Перестраивает список справочника с учётом пользовательских параметров
        /// и текущего фильтра поиска (issue #68).
        /// </summary>
        private void RefreshReference()
        {
            LstReference.ItemsSource = null;
            LstReference.ItemsSource =
                OneCLaunchArgumentParser.FilterReferenceCatalog(BuildReferenceCatalog(), TxtSearch.Text);
            UpdateCustomButtonsState();
        }

        /// <summary>
        /// Строит каталог всех ключей командной строки 1С с описаниями
        /// для справочника в нижней части окна. Логику построения делегирует
        /// общему сервису <see cref="OneCLaunchArgumentParser"/> (ПЗ-5).
        /// </summary>
        private List<OneCLaunchParameterReference> BuildReferenceCatalog()
        {
            return OneCLaunchArgumentParser.BuildReferenceCatalog(_customParams).ToList();
        }

        /// <summary>
        /// Подставляет параметр из справочника в поле «Параметры» по двойному клику.
        /// </summary>
        private void OnReferenceDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (LstReference.SelectedItem is OneCLaunchParameterReference item)
                InsertCustomText(item.Key);
        }

        /// <summary>
        /// Добавляет текст в поле «Параметры», разделяя пробелом.
        /// </summary>
        private void InsertCustomText(string text)
        {
            var updated = OneCLaunchArgumentParser.AppendParameter(TxtCustom.Text, text);
            if (updated == TxtCustom.Text)
                return;

            TxtCustom.Text = updated;
            TxtCustom.CaretIndex = TxtCustom.Text.Length;
            TxtCustom.Focus();
        }

        private void OnOk_Click(object sender, RoutedEventArgs e)
        {
            Result = (TxtCustom.Text ?? string.Empty).Trim();
            DialogResult = true;
        }

        // ============ Пользовательские параметры (issue #141) ============

        /// <summary>Добавляет введённый в поле текст как пользовательский параметр справочника.</summary>
        private void AddCustomParameter()
        {
            var key = (TxtNewParam.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(key))
                return;
            var comment = (TxtNewComment.Text ?? string.Empty).Trim();
            var entry = string.IsNullOrWhiteSpace(comment) ? key : key + "\t" + comment;

            // Проверяем совпадение именно по ключу (до табуляции), а не по всей записи.
            if (_customParams.Any(p =>
                string.Equals((p ?? string.Empty).Split('\t')[0].Trim(), key, StringComparison.OrdinalIgnoreCase)))
                return;
            _customParams.Add(entry);
            TxtNewParam.Clear();
            TxtNewComment.Clear();
            PersistCustomParameters();
            RefreshReference();
            TxtNewParam.Focus();
        }

        /// <summary>Удаляет выбранный пользовательский параметр из справочника.</summary>
        private void RemoveSelectedCustomParameter()
        {
            if (LstReference.SelectedItem is not OneCLaunchParameterReference { IsCustom: true } item)
                return;
            _customParams.RemoveAll(p =>
                string.Equals((p ?? string.Empty).Split('\t')[0].Trim(), item.Key, StringComparison.OrdinalIgnoreCase));
            PersistCustomParameters();
            RefreshReference();
        }

        /// <summary>Сохраняет список пользовательских параметров через обратный вызов.</summary>
        private void PersistCustomParameters()
        {
            _onCustomParametersChanged?.Invoke(_customParams);
        }

        private void OnAddParam_Click(object sender, RoutedEventArgs e) => AddCustomParameter();

        private void OnRemoveParam_Click(object sender, RoutedEventArgs e) => RemoveSelectedCustomParameter();

        private void OnNewParamKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                AddCustomParameter();
                e.Handled = true;
            }
        }

        private void OnReferenceKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                RemoveSelectedCustomParameter();
                e.Handled = true;
            }
        }

        // ============ Поиск, правка и порядок параметров (issue #68) ============

        /// <summary>Текущий выбранный пользовательский параметр справочника (issue #141/#68).</summary>
        private OneCLaunchParameterReference? SelectedCustomParameter =>
            LstReference.SelectedItem as OneCLaunchParameterReference;

        /// <summary>Обновляет доступность кнопок правки/порядка/удаления по выделению.</summary>
        private void UpdateCustomButtonsState()
        {
            var selected = SelectedCustomParameter;
            var isCustom = selected is { IsCustom: true };
            var index = isCustom ? IndexOfCustom(selected!.Key) : -1;

            BtnRemoveParam.IsEnabled = isCustom;
            BtnEditParam.IsEnabled = isCustom;
            BtnMoveUp.IsEnabled = isCustom && index > 0;
            BtnMoveDown.IsEnabled = isCustom && index >= 0 && index < _customParams.Count - 1;
        }

        /// <summary>Индекс пользовательского параметра по ключу (без учёта регистра); -1 — не найден.</summary>
        private int IndexOfCustom(string key)
        {
            for (var i = 0; i < _customParams.Count; i++)
            {
                var (candidate, _) = OneCLaunchArgumentParser.SplitCustomEntry(_customParams[i]);
                if (string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        /// <summary>Поиск по справочнику: перестроение списка с фильтром (issue #68).</summary>
        private void OnSearchTextChanged(object sender, TextChangedEventArgs e) => RefreshReference();

        /// <summary>Правка выбранного пользовательского параметра в отдельном диалоге (issue #68).</summary>
        private void OnEditParam_Click(object sender, RoutedEventArgs e) => EditSelectedCustomParameter();

        private void EditSelectedCustomParameter()
        {
            var selected = SelectedCustomParameter;
            if (selected is not { IsCustom: true })
                return;

            var index = IndexOfCustom(selected.Key);
            if (index < 0)
                return;

            var (oldKey, oldComment) = OneCLaunchArgumentParser.SplitCustomEntry(_customParams[index]);

            var dialog = new LaunchParameterEditWindow(oldKey, oldComment)
            {
                Owner = this
            };
            if (dialog.ShowDialog() != true)
                return;

            var newKey = (dialog.Key ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(newKey))
                return;
            var newComment = (dialog.Comment ?? string.Empty).Trim();

            // Дубликат ключа другим пользовательским параметром недопустим (как при добавлении);
            // правка с сохранением прежнего ключа — не дубликат.
            for (var i = 0; i < _customParams.Count; i++)
            {
                if (i == index)
                    continue;
                var (candidate, _) = OneCLaunchArgumentParser.SplitCustomEntry(_customParams[i]);
                if (string.Equals(candidate, newKey, StringComparison.OrdinalIgnoreCase))
                {
                    System.Windows.MessageBox.Show(
                        LocalizationManager.T("LaunchParams.CustomDuplicate"),
                        LocalizationManager.T("LaunchParams.EditTitle"),
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            _customParams[index] = string.IsNullOrWhiteSpace(newComment) ? newKey : newKey + "\t" + newComment;
            PersistCustomParameters();
            RefreshReference();
            // Восстанавливаем выделение отредактированного параметра.
            LstReference.SelectedItem = _customParamsAsReferences()
                .FirstOrDefault(r => r is { IsCustom: true } &&
                    string.Equals(r.Key, newKey, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Список строк справочника одной коллекцией (для восстановления выделения).</summary>
        private IReadOnlyList<OneCLaunchParameterReference> _customParamsAsReferences()
        {
            var baseList = BuildReferenceCatalog();
            var filtered = OneCLaunchArgumentParser.FilterReferenceCatalog(baseList, TxtSearch.Text);
            return filtered.ToList();
        }

        /// <summary>Перемещает выбранный пользовательский параметр (issue #68).</summary>
        private void MoveSelectedCustomParameter(int delta)
        {
            var selected = SelectedCustomParameter;
            if (selected is not { IsCustom: true })
                return;

            if (!OneCLaunchArgumentParser.MoveCustomParameter(_customParams, selected.Key, delta))
                return;
            PersistCustomParameters();
            RefreshReference();
            // Восстанавливаем выделение перемещённого параметра.
            LstReference.SelectedItem = _customParamsAsReferences()
                .FirstOrDefault(r => r is { IsCustom: true } &&
                    string.Equals(r.Key, selected.Key, StringComparison.OrdinalIgnoreCase));
        }

        private void OnMoveUp_Click(object sender, RoutedEventArgs e) => MoveSelectedCustomParameter(-1);

        private void OnMoveDown_Click(object sender, RoutedEventArgs e) => MoveSelectedCustomParameter(1);

    }
}
#endif