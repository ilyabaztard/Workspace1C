#if WINDOWS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Диалог «Поиск потерянных и забытых баз 1С 8» (issue #247): выбор дисков/корней
    /// поиска, фоновое рекурсивное сканирование в поисках файлов <c>1Cv8.1CD</c> с
    /// кнопкой «Прекратить», таблица найденных баз и добавление отсутствующих в списке
    /// приложения баз. Признаки «В приложении» и «В ibases.v8i» определяются по путям
    /// файловых баз приложения и записям реестра 1С.
    /// </summary>
    public partial class FindLostBasesWindow : Window
    {
        private readonly IReadOnlyList<Infobase> _infobases;
        private readonly Action<Infobase> _addBase;
        private readonly HashSet<string> _appFileDirs = new(FoundBaseRowViewModel.DirComparer);
        private readonly HashSet<string> _v8iFileDirs = new(FoundBaseRowViewModel.DirComparer);
        private readonly List<FoundBaseRowViewModel> _rows = new();
        private readonly IAppLogger _logger = AppServices.GetRequiredService<IAppLogger>();

        private CancellationTokenSource? _cts;

        /// <summary>Признак того, что хотя бы одна база была добавлена (для персиста в настройках).</summary>
        public bool DataChanged { get; private set; }

        /// <param name="infobases">Список баз приложения (для признака «В приложении»).</param>
        /// <param name="addBase">Обратный вызов добавления новой базы в список приложения.</param>
        public FindLostBasesWindow(IReadOnlyList<Infobase> infobases, Action<Infobase> addBase)
        {
            InitializeComponent();
            _infobases = infobases;
            _addBase = addBase;

            BuildAppFileDirs();
            BuildV8iFileDirs();
            PopulateDriveChecks();

            ProgressText.Text = string.Empty;
            SummaryText.Text = string.Empty;
            UpdateCheckedHeader();
            UpdateAddEnabled();
        }

        private static string T(string key) => LocalizationManager.T(key);

        /// <summary>Собирает пути каталогов файловых баз, уже присутствующих в приложении.</summary>
        private void BuildAppFileDirs()
        {
            foreach (var ib in _infobases)
            {
                var dir = InfobaseMaintenanceService.GetFileBaseDirectory(ib);
                if (!string.IsNullOrWhiteSpace(dir))
                    _appFileDirs.Add(FoundBaseRowViewModel.NormalizeDir(dir));
            }
        }

        /// <summary>Собирает пути файловых баз из реестра 1С (ibases.v8i).</summary>
        private void BuildV8iFileDirs()
        {
            var filePath = IbasesV8iImporter.FindDefaultPath();
            if (filePath is null)
                return;

            try
            {
                foreach (var ib in IbasesV8iImporter.ReadInfobases(filePath))
                {
                    if (ib.Connection.Type == ConnectionType.File
                        && !string.IsNullOrWhiteSpace(ib.Connection.FilePath))
                        _v8iFileDirs.Add(FoundBaseRowViewModel.NormalizeDir(ib.Connection.FilePath));
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Не удалось прочитать записи ibases.v8i для признака «В ibases.v8i»", ex);
            }
        }

        /// <summary>Заполняет панель выбора корней поиска флажками (все отмечены).</summary>
        private void PopulateDriveChecks()
        {
            var roots = InfobaseDiskScanner.EnumerateSearchRoots();
            if (roots.Count == 0)
            {
                // Issue #61: текст-заглушка «Нет корней поиска» красится кистью темы,
                // а не фиксированным Gray (нечитаем в тёмной теме).
                DrivesPanel.Children.Add(new TextBlock
                {
                    Text = T("FindLostBases.NoRoots"),
                    Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush"),
                    Margin = new Thickness(4, 4, 4, 4)
                });
                SearchButton.IsEnabled = false;
                return;
            }

            foreach (var root in roots)
            {
                DrivesPanel.Children.Add(new CheckBox
                {
                    Content = root,
                    Tag = root,
                    IsChecked = true,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 4, 6, 4),
                    Cursor = System.Windows.Input.Cursors.Hand
                });
            }
        }

        private void OnDriveCheckAll(object sender, RoutedEventArgs e)
        {
            foreach (var cb in DrivesPanel.Children.OfType<CheckBox>())
                cb.IsChecked = true;
        }

        private void OnDriveCheckNone(object sender, RoutedEventArgs e)
        {
            foreach (var cb in DrivesPanel.Children.OfType<CheckBox>())
                cb.IsChecked = false;
        }

        /// <summary>Открывает диалог выбора каталога и подставляет путь в поле ввода.</summary>
        private void OnBrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = T("FindLostBases.Button.Browse"),
                Multiselect = false
            };
            var current = FolderPathBox.Text?.Trim() ?? string.Empty;
            if (current.Length > 0 && Directory.Exists(current))
                dialog.InitialDirectory = current;

            if (dialog.ShowDialog(this) == true)
                FolderPathBox.Text = dialog.FolderName;
        }

        /// <summary>Ввод Enter в поле пути добавляет каталог как корень поиска.</summary>
        private void OnFolderPath_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
                OnAddFolder_Click(sender, e);
        }

        /// <summary>
        /// Добавляет введённый каталог как дополнительный корень поиска: проверяет его
        /// существование и отсутствие дубликата среди уже добавленных корней, затем
        /// добавляет отмеченный флажок в панель дисков (подхватывается GetSelectedRoots()).
        /// </summary>
        private void OnAddFolder_Click(object sender, RoutedEventArgs e)
        {
            var path = FolderPathBox.Text?.Trim() ?? string.Empty;
            if (path.Length == 0)
                return;

            if (!Directory.Exists(path))
            {
                SummaryText.Text = string.Format(T("FindLostBases.FolderNotExists"), path);
                return;
            }

            var normalized = FoundBaseRowViewModel.NormalizeDir(path);
            var comparer = FoundBaseRowViewModel.DirComparer;
            var isDuplicate = DrivesPanel.Children.OfType<CheckBox>()
                .Any(c => c.Tag is string s && comparer.Equals(normalized, FoundBaseRowViewModel.NormalizeDir(s)));
            if (isDuplicate)
            {
                SummaryText.Text = T("FindLostBases.FolderDuplicate");
                return;
            }

            DrivesPanel.Children.Add(new CheckBox
            {
                Content = path,
                Tag = path,
                IsChecked = true,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 4, 6, 4),
                Cursor = System.Windows.Input.Cursors.Hand
            });
            FolderPathBox.Clear();
            SummaryText.Text = string.Empty;
        }

        /// <summary>Отмечает все найденные базы, доступные для добавления (не в приложении).</summary>
        private void OnFoundCheckAll(object sender, RoutedEventArgs e)
        {
            foreach (var r in _rows)
            {
                if (r.NotInApp)
                    r.IsChecked = true;
            }
            UpdateCheckedHeader();
            UpdateAddEnabled();
        }

        /// <summary>Снимает отметки со всех найденных баз, доступных для добавления.</summary>
        private void OnFoundCheckNone(object sender, RoutedEventArgs e)
        {
            foreach (var r in _rows)
            {
                if (r.NotInApp)
                    r.IsChecked = false;
            }
            UpdateCheckedHeader();
            UpdateAddEnabled();
        }

        /// <summary>Выбранные пользователем корни поиска.</summary>
        private List<string> GetSelectedRoots()
        {
            return DrivesPanel.Children.OfType<CheckBox>()
                .Where(c => c.IsChecked == true && c.Tag is string s && !string.IsNullOrWhiteSpace(s))
                .Select(c => (string)c.Tag!)
                .ToList();
        }

        private void OnStop_Click(object sender, RoutedEventArgs e)
            => _cts?.Cancel();

        private void OnClose_Click(object sender, RoutedEventArgs e)
            => Close();

        /// <summary>Обновляет заголовок колонки-флажка счётчиком отмеченных элементов.</summary>
        private void UpdateCheckedHeader()
        {
            CheckedHeaderText.Text = string.Format(
                LocalizationManager.T("FindLostBases.Column.CheckedHeaderFormat"),
                _rows.Count(r => r.IsChecked));
        }

        /// <summary>
        /// Обновляет доступность кнопки «Добавить в список баз»: она активна, если
        /// отмечена хотя бы одна добавляемая база и выбрано хотя бы одно назначение.
        /// Метод может вызываться из обработчиков <c>Checked</c>/<c>Unchecked</c> флажков
        /// назначения ещё во время <see cref="InitializeComponent"/>, когда элементы окна
        /// созданы не полностью, поэтому защищаемся от нулевых ссылок (issue: NRE при старте).
        /// </summary>
        private void UpdateAddEnabled()
        {
            if (AddButton is null || AddToAppCheckBox is null || AddToV8iCheckBox is null)
                return;

            var anyDestination = AddToAppCheckBox.IsChecked == true || AddToV8iCheckBox.IsChecked == true;
            AddButton.IsEnabled = anyDestination && _rows.Any(r => r.IsChecked && !r.InApp);
        }

        /// <summary>Пересчитывает доступность кнопки «Добавить» при изменении флажков назначения.</summary>
        private void OnDestinationChanged(object sender, RoutedEventArgs e)
            => UpdateAddEnabled();

        /// <summary>Показывает ход сканирования на UI-потоке.</summary>
        private void OnScanProgress(int found, int dirs)
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                new Action(() =>
                {
                    ProgressText.Text = string.Format(
                        LocalizationManager.T("FindLostBases.ProgressFormat"), found, dirs);
                }));
        }

        /// <summary>
        /// Переносит накопленные в буфере найденные базы в таблицу (на UI-потоке).
        /// Вызывается таймером пакетного обновления (~100 мс) и по завершении сканирования,
        /// чтобы строки появлялись инкрементально и не терялись при отмене.
        /// </summary>
        private void FlushFoundRows(object gate, List<FoundBaseRowViewModel> pending)
        {
            List<FoundBaseRowViewModel>? batch = null;
            lock (gate)
            {
                if (pending.Count > 0)
                {
                    batch = new List<FoundBaseRowViewModel>(pending);
                    pending.Clear();
                }
            }

            if (batch is null || batch.Count == 0)
                return;

            _rows.AddRange(batch);
            BasesGrid.ItemsSource = _rows;
            SummaryText.Text = string.Format(
                LocalizationManager.T("FindLostBases.FoundFormat"), _rows.Count);
            UpdateCheckedHeader();
            UpdateAddEnabled();
        }

        private async void OnSearch_Click(object sender, RoutedEventArgs e)
        {
            var roots = GetSelectedRoots();
            if (roots.Count == 0)
            {
                SummaryText.Text = LocalizationManager.T("FindLostBases.NoneSelected");
                return;
            }

            SearchButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            SetDriveChecksEnabled(false);
            _rows.Clear();
            BasesGrid.ItemsSource = null;
            ProgressText.Text = string.Empty;
            SummaryText.Text = string.Empty;
            UpdateCheckedHeader();
            UpdateAddEnabled();

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            // Буфер найденных строк + таймер пакетного обновления таблицы (~100 мс):
            // строки появляются инкрементально по мере сканирования, а не разом в конце.
            var gate = new object();
            var pending = new List<FoundBaseRowViewModel>();
            var flushTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            flushTimer.Tick += (_, _) => FlushFoundRows(gate, pending);
            flushTimer.Start();

            try
            {
                await Task.Run(() => InfobaseDiskScanner.Scan(roots, token, OnScanProgress,
                    found =>
                    {
                        var row = new FoundBaseRowViewModel(found)
                        {
                            InApp = _appFileDirs.Contains(FoundBaseRowViewModel.NormalizeDir(found.DirectoryPath)),
                            InIbasesV8i = _v8iFileDirs.Contains(FoundBaseRowViewModel.NormalizeDir(found.DirectoryPath))
                        };
                        lock (gate)
                            pending.Add(row);
                    }), token);

                FlushFoundRows(gate, pending);
                SummaryText.Text = string.Format(
                    LocalizationManager.T("FindLostBases.FoundFormat"), _rows.Count);
                UpdateCheckedHeader();
                UpdateAddEnabled();
            }
            catch (OperationCanceledException)
            {
                FlushFoundRows(gate, pending);
                SummaryText.Text = LocalizationManager.T("FindLostBases.Stopped");
                UpdateCheckedHeader();
                UpdateAddEnabled();
            }
            catch (Exception ex)
            {
                FlushFoundRows(gate, pending);
                _logger.Error("Ошибка сканирования дисков в поисках баз 1С", ex);
                SummaryText.Text = string.Format(LocalizationManager.T("FindLostBases.ErrorFormat"), ex.Message);
            }
            finally
            {
                flushTimer.Stop();
                _cts.Dispose();
                _cts = null;
                SearchButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                SetDriveChecksEnabled(true);
                ProgressText.Text = string.Empty;
            }
        }

        /// <summary>
        /// Добавляет отмеченные базы, отсутствующие в приложении, в выбранные назначения:
        /// в список приложения и/или в файл ibases.v8i (по отдельности или вместе).
        /// </summary>
        private void OnAdd_Click(object sender, RoutedEventArgs e)
        {
            var addToApp = AddToAppCheckBox.IsChecked == true;
            var addToV8i = AddToV8iCheckBox.IsChecked == true;

            if (!addToApp && !addToV8i)
            {
                SummaryText.Text = T("FindLostBases.NoneDestination");
                return;
            }

            var checkedRows = _rows.Where(r => r.IsChecked).ToList();
            var toAdd = checkedRows.Where(r => !r.InApp).ToList();
            var alreadyInList = checkedRows.Count(r => r.InApp);

            var addedApp = 0;
            var forV8i = new List<Infobase>();
            var rowsForV8i = new List<FoundBaseRowViewModel>();

            foreach (var row in toAdd)
            {
                var ib = new Infobase
                {
                    Name = row.Name,
                    Group = string.Empty,
                    Id = Guid.NewGuid().ToString("D"),
                    Connection = new ConnectionSettings
                    {
                        Type = ConnectionType.File,
                        FilePath = row.Base.DirectoryPath
                    }
                };

                if (addToApp)
                {
                    _addBase(ib);
                    addedApp++;

                    var dirKey = FoundBaseRowViewModel.NormalizeDir(row.Base.DirectoryPath);
                    _appFileDirs.Add(dirKey);
                    row.InApp = true;
                    DataChanged = true;
                }

                if (addToV8i)
                {
                    forV8i.Add(ib);
                    rowsForV8i.Add(row);
                }
            }

            var addedV8i = 0;
            var v8iMessage = string.Empty;
            if (addToV8i && forV8i.Count > 0)
            {
                var v8iPath = IbasesV8iImporter.FindDefaultPath();
                if (v8iPath is null)
                {
                    v8iMessage = T("FindLostBases.NoV8iPath");
                }
                else
                {
                    try
                    {
                        IbasesV8iExporter.AddInfobasesToFile(v8iPath, forV8i, new List<Group>());
                        addedV8i = forV8i.Count;
                        for (var i = 0; i < forV8i.Count; i++)
                        {
                            var row = rowsForV8i[i];
                            row.InIbasesV8i = true;
                            _v8iFileDirs.Add(FoundBaseRowViewModel.NormalizeDir(row.Base.DirectoryPath));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error("Ошибка записи выбранных баз в ibases.v8i", ex);
                        v8iMessage = string.Format(T("FindLostBases.ErrorFormat"), ex.Message);
                    }
                }
            }

            // Снимаем отметки со строк, добавленных хотя бы в одно из назначений.
            foreach (var row in toAdd)
            {
                if (row.InApp || rowsForV8i.Contains(row))
                    row.IsChecked = false;
            }

            SummaryText.Text = string.IsNullOrEmpty(v8iMessage)
                ? string.Format(T("FindLostBases.AddedFormatSplit"), addedApp, addedV8i, alreadyInList)
                : v8iMessage;
            UpdateCheckedHeader();
            UpdateAddEnabled();
        }

        private void OnCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit)
                return;
            UpdateCheckedHeader();
            UpdateAddEnabled();
        }

        private void SetDriveChecksEnabled(bool enabled)
        {
            foreach (var cb in DrivesPanel.Children.OfType<CheckBox>())
                cb.IsEnabled = enabled;
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            _cts?.Cancel();
            base.OnClosing(e);
        }
    }
}
#endif