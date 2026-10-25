using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Серверы 1С» (0.3.9.124, Windows/WPF): встроенный монитор серверов 1С
    /// через утилиту rac. Слева — справочник «Сервера управления» (issue #3): список
    /// серверов (только Описание) с кнопками «Добавить/Изменить/Удалить»; при активации
    /// строки выполняется подключение к кластеру. Справа — выбор кластера, вкладки
    /// «Рабочие процессы / Сеансы / Соединения / Блокировки / Регламентные задания /
    /// Информация о кластере» и статус-строка. Вся логика — в чистой ViewModel
    /// <see cref="ServerMonitorViewModel"/>. Адрес/порт/логин/пароль хранятся в
    /// справочнике (server_list.json) и на форме не показываются.
    /// </summary>
    public partial class ServerMonitorWindow : Window
    {
        private readonly ServerMonitorViewModel _vm;

        /// <summary>
        /// Открытие «Импорта из кластера 1С» (issue #27): делегат MainViewModel —
        /// окно монитора к репозиторию напрямую не обращается.
        /// </summary>
        private readonly Action<ManagementServer?, Guid?>? _openClusterImport;

        /// <summary>
        /// Открытие «Миграции между кластерами 1С» (issue #41): делегат MainViewModel —
        /// окно монитора к репозиторию напрямую не обращается.
        /// </summary>
        private readonly Action<ManagementServer?, Guid?>? _openClusterMigration;

        /// <summary>Фоновое обновление кэша статусов служб (5 с; issue #3, часть 2).</summary>
        private System.Threading.Timer? _servicesStatusTimer;

        private static readonly TimeSpan ServicesStatusRefreshInterval = TimeSpan.FromSeconds(5);

        /// <param name="openClusterImport">
        /// Открытие окна импорта с сервером и выбранным кластером, текущими на момент
        /// нажатия «Импорт» (issue #27; кластер — issue #25); null — кнопка «Импорт»
        /// под списком баз скрыта/не активна.
        /// </param>
        /// <param name="openClusterMigration">
        /// Открытие окна миграции с сервером и выбранным кластером, текущими на момент
        /// нажатия «Миграция» (issue #41); null — кнопка «Миграция» под списком баз
        /// скрыта/не активна.
        /// </param>
        public ServerMonitorWindow(
            Action<ManagementServer?, Guid?>? openClusterImport = null,
            Action<ManagementServer?, Guid?>? openClusterMigration = null)
        {
            InitializeComponent();
            _openClusterImport = openClusterImport;
            _openClusterMigration = openClusterMigration;

            var rac = AppServices.GetRequiredService<IRacClient>();
            var dialogs = AppServices.GetRequiredService<IDialogService>();
            var serverListStore = AppServices.GetRequiredService<IServerListStore>();

            // issue #69: диалог повтора удаления базы открывается из VM (после неудачного
            // rac «infobase drop») — колбэк реализует окно, чтобы VM оставалась тестируемой.
            _vm = new ServerMonitorViewModel(
                rac,
                dialogs,
                action => Application.Current?.Dispatcher.BeginInvoke(action),
                serverListStore,
                (errorText, login) =>
                {
                    var retry = new RacInfobaseDropRetryWindow(errorText, login) { Owner = this };
                    return retry.ShowDialog() == true
                        ? new ViewModels.DropRetryResult(retry.Retry, retry.User, retry.Password)
                        : ViewModels.DropRetryResult.Cancel;
                });

            DataContext = _vm;
            Title = LocalizationManager.T("ServerMonitor.Title");
            // Кнопка «Закрыть» на панели подключения убрана (issue #324) — есть кнопка ✕
            // в заголовке окна. Esc сохранён: раньше его давала IsCancel у панельной
            // кнопки, теперь обрабатываем вручную (фокус может быть в текстовом поле).
            PreviewKeyDown += OnWindowPreviewKeyDown;
            // issue #3: таблицы сеансов/соединений/блокировок биндятся на ОТФИЛЬТРОВАННЫЕ
            // коллекции — выбор базы фильтрует их на клиенте без перезагрузки кластера.
            // Список баз и заголовки вкладок привязаны в XAML (Infobases, *TabText).
            SessionsGrid.ItemsSource = _vm.FilteredSessions;
            ConnectionsGrid.ItemsSource = _vm.FilteredConnections;
            LocksGrid.ItemsSource = _vm.FilteredLocks;
            ProcessesGrid.ItemsSource = _vm.Processes;

            // Состояние окна запоминается (issue #3, комментарий 7OH): максимизировано или нет.
            try
            {
                var repository = AppServices.GetRequiredService<IInfobaseRepository>();
                var settings = repository.LoadSettings();
                if (settings.ServerMonitorMaximized)
                    WindowState = WindowState.Maximized;
            }
            catch
            {
                // Запоминание состояния — вспомогательная возможность, сбой не важен.
            }

            // Таймер автообновления останавливается при закрытии окна (без утечки);
            // состояние «максимизировано» сохраняется в настройки.
            Closed += (_, _) =>
            {
                // Фоновый таймер статусов служб останавливается при закрытии окна (без утечки).
                _servicesStatusTimer?.Dispose();
                _servicesStatusTimer = null;
                _vm.Dispose();
                try
                {
                    var repository = AppServices.GetRequiredService<IInfobaseRepository>();
                    var settings = repository.LoadSettings();
                    settings.ServerMonitorMaximized = WindowState == WindowState.Maximized;
                    repository.SaveSettings(settings);
                }
                catch
                {
                    // Запоминание состояния — вспомогательная возможность, сбой не важен.
                }
            };
        }

        /// <summary>
        /// Двойной клик по строке справочника «Сервера управления» (issue #3): подключение
        /// к кластеру сервера (адрес/порт/логин/пароль из записи). Простое выделение строки
        /// подключение НЕ запускает — строку можно спокойно выделять для правки.
        /// </summary>
        private async void OnServersList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ServersList.SelectedItem is ManagementServer server)
                await _vm.ConnectToServerAsync(server);
        }

        /// <summary>
        /// «Подключиться» (issue #3): подключение к кластеру выбранного сервера
        /// справочника (если строка выделена); без выделения — по текущим полям.
        /// </summary>
        private void OnConnect_Click(object sender, RoutedEventArgs e) =>
            _vm.ConnectCommand.Execute(null);

        private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        }

        private void OnRefresh_Click(object sender, RoutedEventArgs e) => _vm.Refresh();

        private void OnTerminateSession_Click(object sender, RoutedEventArgs e) =>
            _vm.TerminateSessionCommand.Execute(null);

        private void OnDisconnectConnection_Click(object sender, RoutedEventArgs e) =>
            _vm.DisconnectConnectionCommand.Execute(null);

        /// <summary>
        /// «Сбросить» фильтр по базе (issue #3): клиентская операция — показываются
        /// сеансы/соединения/блокировки всех баз кластера, без rac-вызовов.
        /// </summary>
        private void OnClearInfobaseFilter_Click(object sender, RoutedEventArgs e) =>
            _vm.ClearInfobaseFilter();

        /// <summary>
        /// Сортировка колонок DataGrid (issue #3): явно управляем индикаторами — стрелка
        /// показывается у активной колонки, у остальных сбрасывается. Саму сортировку
        /// выполняет DataGrid (e.Handled = false), направление он переключает сам.
        /// </summary>
        /// <summary>Список баз по алфавиту (issue #3, кнопка «А↓»).</summary>
        private void OnSortInfobasesAsc_Click(object sender, RoutedEventArgs e) => _vm.SortInfobases(false);

        /// <summary>Список баз в обратном порядке (issue #3, кнопка «Я↓»).</summary>
        private void OnSortInfobasesDesc_Click(object sender, RoutedEventArgs e) => _vm.SortInfobases(true);

        private void OnGridSorting(object sender, DataGridSortingEventArgs e)
        {
            var grid = (DataGrid)sender;
            foreach (var column in grid.Columns)
            {
                if (column != e.Column)
                    column.SortDirection = null;
            }
            e.Handled = false;
        }

        /// <summary>
        /// «Сохранить изменения» на вкладке «Информация о кластере» (issue #324, C3):
        /// rac «cluster update» по изменённым параметрам, перечитывание данных.
        /// </summary>
        private void OnSaveClusterInfo_Click(object sender, RoutedEventArgs e) =>
            _vm.SaveClusterPropertiesCommand.Execute(null);

        /// <summary>
        /// «Диагностика сети…» (0.3.9.232, функция 12): открывает окно диагностики
        /// с адресом/портом выбранного сервера справочника. Не требует успешного
        /// подключения rac — диагностика нужна именно до подключения.
        /// </summary>
        private void OnDiagnostics_Click(object sender, RoutedEventArgs e)
        {
            var target = NetworkDiagnosticsTargets.FromServerMonitor(_vm.ServerAddress, _vm.ServerPort);
            var vm = new NetworkDiagnosticsViewModel(
                AppServices.GetRequiredService<INetworkDiagnosticsService>(),
                target,
                action => Application.Current?.Dispatcher.BeginInvoke(action));
            new NetworkDiagnosticsWindow(vm) { Owner = this }.ShowDialog();
        }

        // ===================== Базы кластера (issue #3) =====================

        /// <summary>
        /// «Добавить базу»: модальный диалог создания информационной базы (rac
        /// «infobase create») → создание в выбранном кластере → обновление списков.
        /// </summary>
        private async void OnAddInfobase_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new RacInfobaseCreateWindow() { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Result is not { } request)
                return;
            await _vm.CreateInfobaseAsync(request);
        }

        /// <summary>
        /// «Изменить базу» (issue #3, комментарий 7OH): модальный диалог правки свойств
        /// (поля предзаполнены, rac «infobase update») → изменение → обновление списков.
        /// </summary>
        private async void OnEditInfobase_Click(object sender, RoutedEventArgs e)
        {
            if (_vm.SelectedInfobase is not { } row || row.InfobaseId == Guid.Empty)
                return;
            // Полный диалог свойств (issue #3, 7OH 17.10): «infobase info» с учётными
            // данными администратора ИБ из списка баз приложения либо введёнными вручную.
            System.Collections.Generic.IEnumerable<Infobase>? bases = null;
            try
            {
                bases = AppServices.GetRequiredService<IInfobaseRepository>().Load();
            }
            catch
            {
                // Список баз недоступен — учётные данные пользователь введёт сам.
            }
            var propertiesVm = _vm.CreateInfobasePropertiesViewModel(SummaryFromRow(row), bases);
            var dialog = new RacInfobasePropertiesWindow(propertiesVm) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Result is not { } update)
                return;
            await _vm.UpdateSelectedInfobaseAsync(update);
        }

        /// <summary>Строит «infobase summary» из строки списка для предзаполнения диалога правки.</summary>
        private static RacInfobaseSummary SummaryFromRow(RacInfobaseRow row) => new()
        {
            InfobaseId = row.InfobaseId,
            Name = row.Name,
            // RacInfobaseRow подставляет «—» для пустых строк — в диалог возвращаем пустоту.
            Descr = Unplacehold(row.Descr),
            Dbms = row.Dbms,
            DbServer = row.DbServer,
            DbName = row.DbName,
            DbUser = Unplacehold(row.DbUser),
            Locale = Unplacehold(row.Locale),
            SecurityLevel = row.SecurityLevel
        };

        private static string Unplacehold(string value) =>
            string.Equals(value, "—", StringComparison.Ordinal) ? string.Empty : value;

        /// <summary>
        /// «Удалить базу» (issue #3, комментарий 7OH от 2026-10-17): диалог удаления
        /// с выбором «Просто удалить из кластера» / «Удалить с удалением на сервере
        /// СУБД» (в кластере бывают только клиент-серверные базы, 7OH 18.10 20:24) →
        /// rac «infobase drop [--drop-database]» → обновление.
        /// </summary>
        private async void OnDeleteInfobase_Click(object sender, RoutedEventArgs e)
        {
            if (_vm.SelectedInfobase is not { } row)
                return;
            var dialog = new RacInfobaseDropWindow(
                row.Name, ServerMonitorViewModel.CanDropDatabaseOnServer(row), row.Dbms, row.DbServer)
            {
                Owner = this
            };
            if (dialog.ShowDialog() != true)
                return;
            await _vm.DropSelectedInfobaseAsync(dialog.DropDatabase);
        }

        /// <summary>
        /// «Импорт» (issue #27): открывает «Импорт из кластера 1С» с текущим сервером —
        /// выбранный в списке, иначе подключённый, иначе первый из справочника. При
        /// пустом справочнике — подсказка и выход. Сам импорт выполняет делегат
        /// MainViewModel (логика добавления баз/групп — там, окно к репозиторию не ходит).
        /// </summary>
        private void OnImport_Click(object sender, RoutedEventArgs e)
        {
            var server = _vm.SelectedServer ?? _vm.ConnectedServer ?? _vm.Servers.FirstOrDefault();
            if (server is null)
            {
                MessageBox.Show(
                    LocalizationManager.T("ClusterImport.ServersEmpty"),
                    LocalizationManager.T("ServerMonitor.Title"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _openClusterImport?.Invoke(server, _vm.SelectedClusterId);
        }

        /// <summary>
        /// «Миграция» (issue #41): открывает «Миграцию между кластерами 1С» с текущим
        /// сервером — выбранный в списке, иначе подключённый, иначе первый из справочника.
        /// При пустом справочнике — подсказка и выход. Сам перенос выполняет делегат
        /// MainViewModel (логика добавления баз/групп — там, окно к репозиторию не ходит).
        /// </summary>
        private void OnMigrate_Click(object sender, RoutedEventArgs e)
        {
            var server = _vm.SelectedServer ?? _vm.ConnectedServer ?? _vm.Servers.FirstOrDefault();
            if (server is null)
            {
                MessageBox.Show(
                    LocalizationManager.T("ClusterImport.ServersEmpty"),
                    LocalizationManager.T("ServerMonitor.Title"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _openClusterMigration?.Invoke(server, _vm.SelectedClusterId);
        }

        // ===================== Справочник «Сервера управления» (issue #3) =====================

        /// <summary>«Добавить»: модальный редактор новой записи → добавление в справочник.</summary>
        private void OnAddServer_Click(object sender, RoutedEventArgs e)
        {
            var edit = new ServerEditWindow() { Owner = this };
            if (edit.ShowDialog() != true || edit.Result is not { } created)
                return;
            _vm.AddServer(created);
        }

        /// <summary>«Изменить»: модальный редактор выбранной записи (правка на копии).</summary>
        private void OnEditServer_Click(object sender, RoutedEventArgs e)
        {
            if (ServersList.SelectedItem is not ManagementServer server)
                return;
            var edit = new ServerEditWindow(CloneServer(server)) { Owner = this };
            if (edit.ShowDialog() != true || edit.Result is not { } updated)
                return; // Отмена — модель не изменялась.
            _vm.UpdateServer(updated);
        }

        /// <summary>«Удалить»: подтверждение → удаление записи из справочника.</summary>
        private void OnDeleteServer_Click(object sender, RoutedEventArgs e)
        {
            if (ServersList.SelectedItem is not ManagementServer server)
                return;
            if (!AppServices.GetRequiredService<IDialogService>().Confirm(
                    string.Format(LocalizationManager.T("ServerMonitor.Servers.ConfirmDelete"), server.Description),
                    LocalizationManager.T("ServerMonitor.Servers.Title")))
                return;
            _vm.RemoveServer(server);
        }

        /// <summary>«Вверх»: переместить выбранный сервер на позицию выше (issue #3).</summary>
        private void OnMoveServerUp_Click(object sender, RoutedEventArgs e) => _vm.MoveServerUp();

        /// <summary>«Вниз»: переместить выбранный сервер на позицию ниже (issue #3).</summary>
        private void OnMoveServerDown_Click(object sender, RoutedEventArgs e) => _vm.MoveServerDown();

        /// <summary>Глубокая копия записи (для правки без мутации исходного экземпляра).</summary>
        private static ManagementServer CloneServer(ManagementServer source) => new()
        {
            Id = source.Id,
            Description = source.Description,
            Address = source.Address,
            Port = source.Port,
            User = source.User,
            Password = source.Password,
        };

        // ===================== Кнопка «СЛУЖБЫ» (issue #3, 18.10 08:57) =====================
        // Кнопка внизу левой колонки справочника серверов показывает подменю служб —
        // то же, что в трее по Ctrl+ПКМ. Данные строит переиспользуемый
        // ServiceTrayMenuBuilder, результат адаптируется к WPF ContextMenu.

        /// <summary>«СЛУЖБЫ»: показывает подменю служб у кнопки (переиспользование построителя трей-меню).</summary>
        private void OnServicesMenu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button)
                return;
            var menu = new ContextMenu
            {
                PlacementTarget = button,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
                // Скин (issue #3, часть 2): тот же стиль, что у меню служб главного окна.
                Style = TryFindResource("ModernContextMenu") as Style,
            };
            foreach (var item in BuildServicesMenuItems())
                menu.Items.Add(ToWpfServiceMenuItem(item));
            menu.IsOpen = true;
        }

        /// <summary>
        /// Пункты подменю «Службы» по данным services.json и статусам из кэша
        /// (issue #3, часть 2): построение не блокирует UI — статусы читаются
        /// из <see cref="ServiceStatusCache"/> и обновляются фоном (как в трее).
        /// </summary>
        private List<ServiceMenuItem> BuildServicesMenuItems()
        {
            var services = AppServices.TryGetService<IServiceControllerService>();
            var store = AppServices.TryGetService<IServicesStore>();
            var data = store?.Load() ?? new ServiceStoreData();
            var builder = new ServiceTrayMenuBuilder();
            if (services is null)
            {
                // Контроллер недоступен — команды неактивны; остаются «Настроить…»
                // и «Все сервисы» с пустым набором.
                return builder.Build(data, _ => new ServiceInfo { Status = ServiceStatus.Unknown }, ServicePlatform.Windows);
            }
            // Мгновенный показ: статусы из кэша (фоновый таймер держит их свежими).
            EnsureServicesStatusTimer();
            return builder.Build(data,
                name => ServicesStatusCache()?.GetStatus(name) ?? new ServiceInfo { ServiceName = name, Status = ServiceStatus.Unknown },
                ServicePlatform.Windows);
        }

        /// <summary>Единый кэш статусов из DI (issue #3, часть 2): тот же экземпляр,
        /// что у трея и главного окна.</summary>
        private Services.ServiceStatusCache? ServicesStatusCache()
            => AppServices.TryGetService<Services.ServiceStatusCache>();

        /// <summary>Запускает фоновый таймер обновления кэша статусов (5 с).
        /// Опросы — в рабочих потоках, UI не блокируется.</summary>
        private void EnsureServicesStatusTimer()
        {
            if (_servicesStatusTimer is not null)
                return;
            _servicesStatusTimer = new System.Threading.Timer(
                _ => RefreshServicesStatusesInBackground(),
                null,
                TimeSpan.Zero, // первый опрос сразу — прогрев кэша
                ServicesStatusRefreshInterval);
        }

        /// <summary>Фоновое обновление кэша статусов по списку из services.json.</summary>
        private void RefreshServicesStatusesInBackground()
        {
            var cache = ServicesStatusCache();
            var store = AppServices.TryGetService<IServicesStore>();
            if (cache is null || store is null)
                return;
            var data = store.Load();
            var names = ServiceStoreData.NormalizeOrder(data)
                .Where(k => !ServiceStoreData.IsGroupKey(k))
                .Concat(data.Groups.SelectMany(g => g.Services))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (names.Count == 0)
                return;
            _ = cache.RefreshAsync(names);
        }

        /// <summary>
        /// Адаптер пункта модели меню служб к WPF-элементу ContextMenu: те же иконки,
        /// что в контекстном меню основного окна — кружок статуса для службы, папка
        /// для группы, шестерёнка у «Настроить…», цветные глифы команд (1.0.0.14).
        /// Стиль скина ModernMenuItem — как у остальных контекстных меню (issue #3, часть 2).
        /// </summary>
        private object ToWpfServiceMenuItem(ServiceMenuItem item)
        {
            switch (item.Kind)
            {
                case ServiceMenuItemKind.Separator:
                    return new Separator();

                case ServiceMenuItemKind.Settings:
                {
                    // Экранирование '_' (WPF AccessKey; issue #11, 7OH 19.10).
                    var settings = new MenuItem
                    {
                        Header = ServiceTrayMenuBuilder.EscapeMenuText(item.Text),
                        Style = TryFindResource("ModernMenuItem") as Style,
                    };
                    settings.Icon = MenuIconText("⚙", "#6B7280");
                    settings.Click += (_, _) => ShowServicesSettings();
                    return settings;
                }

                case ServiceMenuItemKind.ServiceSubmenu:
                {
                    var submenu = new MenuItem
                    {
                        Header = ServiceTrayMenuBuilder.EscapeMenuText(item.Text),
                        IsEnabled = item.IsEnabled,
                        Style = TryFindResource("ModernMenuItem") as Style,
                    };
                    if (!string.IsNullOrWhiteSpace(item.StatusColorHex))
                        submenu.Icon = MenuStatusCircle(item.StatusColorHex);
                    foreach (var child in item.Children)
                        submenu.Items.Add(ToWpfServiceMenuItem(child));
                    return submenu;
                }

                case ServiceMenuItemKind.GroupSubmenu:
                case ServiceMenuItemKind.AllServicesSubmenu:
                {
                    var submenu = new MenuItem
                    {
                        Header = ServiceTrayMenuBuilder.EscapeMenuText(item.Text),
                        IsEnabled = item.IsEnabled,
                        Style = TryFindResource("ModernMenuItem") as Style,
                    };
                    // Группа — «микс» папка + кружок статуса (замечание 7OH 19.10),
                    // как в трее и в контекстном меню служб главного окна; «Все сервисы» — ☰.
                    submenu.Icon = item.Kind == ServiceMenuItemKind.GroupSubmenu
                        ? MenuGroupIcon(item.StatusColorHex)
                        : MenuIconText("☰", "#7C3AED");
                    foreach (var child in item.Children)
                        submenu.Items.Add(ToWpfServiceMenuItem(child));
                    return submenu;
                }

                case ServiceMenuItemKind.ServiceCommand:
                case ServiceMenuItemKind.GroupCommand:
                case ServiceMenuItemKind.AllServicesCommand:
                {
                    var command = new MenuItem
                    {
                        Header = ServiceTrayMenuBuilder.EscapeMenuText(item.Text),
                        IsEnabled = item.IsEnabled,
                        Style = TryFindResource("ModernMenuItem") as Style,
                    };
                    var action = item.Action ?? ServiceAction.Start;
                    command.Icon = MenuIconText(ServiceCommandGlyphs.Glyph(action), ServiceCommandGlyphs.ColorHex(action));
                    var target = item.Target;
                    var targetName = item.TargetName;
                    var names = item.ServiceNames;
                    command.Click += (_, _) => ExecuteServicesOperation(action, target, targetName, names);
                    return command;
                }

                default:
                    return new MenuItem
                    {
                        Header = ServiceTrayMenuBuilder.EscapeMenuText(item.Text),
                        IsEnabled = item.IsEnabled,
                        Style = TryFindResource("ModernMenuItem") as Style,
                    };
            }
        }

        /// <summary>Цветная иконка пункта меню (глиф-текст, как в контекстном меню основного окна).</summary>
        private static TextBlock MenuIconText(string text, string colorHex) => new()
        {
            Text = text,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        /// <summary>Кружок статуса службы в пункте меню (цвет StatusColorHex).</summary>
        private static Border MenuStatusCircle(string colorHex) => new()
        {
            Width = 9,
            Height = 9,
            CornerRadius = new CornerRadius(4.5),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex)),
            VerticalAlignment = VerticalAlignment.Center
        };

        /// <summary>
        /// Иконка группы служб в пункте меню — «микс» (замечание 7OH 19.10): папка 📦 +
        /// кружок статуса группы справа-снизу (цвет StatusColorHex — зелёный/жёлтый/серый),
        /// как в трее и в контекстном меню служб главного окна. При отсутствии статуса
        /// (кэш пуст, локальная машина) — обычная папка без кружка.
        /// </summary>
        private static FrameworkElement MenuGroupIcon(string statusColorHex)
        {
            var grid = new Grid { Width = 16, Height = 16 };
            grid.Children.Add(new TextBlock
            {
                Text = "📦",
                FontSize = 13,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7C3AED")),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
            if (!string.IsNullOrWhiteSpace(statusColorHex))
            {
                grid.Children.Add(new Border
                {
                    Width = 6,
                    Height = 6,
                    CornerRadius = new CornerRadius(3),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(statusColorHex)),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 1, 1),
                    BorderBrush = new SolidColorBrush(Colors.White),
                    BorderThickness = new Thickness(1)
                });
            }
            return grid;
        }

        /// <summary>«Настроить…»: окно «Управление службами» (модально к монитору).</summary>
        private void ShowServicesSettings()
        {
            var window = new ServicesWindow { Owner = this };
            window.ShowDialog();
        }

        /// <summary>
        /// Выполняет операцию над целевыми службами (отдельная служба / члены группы /
        /// все службы списка): ServiceController блокирующий — работа в фоновом потоке.
        /// После завершения кэш статусов сбрасывается и опрашивается заново (как в трее).
        /// </summary>
        private void ExecuteServicesOperation(
            ServiceAction action,
            ServiceMenuItemTarget target,
            string targetName,
            IReadOnlyList<string> serviceNames)
        {
            var services = AppServices.TryGetService<IServiceControllerService>();
            if (services is null)
                return;

            var names = target switch
            {
                ServiceMenuItemTarget.Group or ServiceMenuItemTarget.All => serviceNames.ToList(),
                _ => new List<string> { targetName }
            };
            if (names.Count == 0)
                return;

            Task.Run(() =>
            {
                foreach (var name in names)
                {
                    try
                    {
                        // Статусы актуализируем на момент операции: доступность могла измениться.
                        var info = services.GetStatus(name);
                        if (!ServiceActionAvailability.CanExecute(
                                action, info.Status, info.CanPauseAndContinue, info.CanStop, ServicePlatform.Windows))
                            continue;
                        switch (action)
                        {
                            case ServiceAction.Start: services.Start(name); break;
                            case ServiceAction.Stop: services.Stop(name); break;
                            case ServiceAction.Pause: services.Pause(name); break;
                            case ServiceAction.Continue: services.Continue(name); break;
                            case ServiceAction.Restart: services.Restart(name); break;
                        }
                    }
                    catch
                    {
                        // Отдельная служба не должна останавливать остальные.
                    }
                }
            }).ContinueWith(_ =>
            {
                // Статусы изменились — сбрасываем кэш и опрашиваем заново в фоне.
                AppServices.TryGetService<ServiceStatusCache>()?.Invalidate();
                RefreshServicesStatusesInBackground();
            }, TaskScheduler.Default);
        }
    }
}
