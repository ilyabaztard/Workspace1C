#if LINUX
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C
{
    /// <summary>
    /// Системный трей главного окна (Avalonia/Linux): создание значка, меню,
    /// обновление по данным и увод окна в трей.
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// Можно ли вернуть спрятанное окно. Значок трея это первый путь, но
        /// на GNOME Shell без AppIndicator он не появится, а ошибки при этом не
        /// будет. Второй путь, повторный запуск приложения через файл-сигнал,
        /// работает только пока включён режим единственного экземпляра.
        /// Берётся состояние текущего процесса, а не настройка: блокировка
        /// и слушатель сигнала заводятся один раз при старте, и снятый на ходу
        /// флажок «несколько экземпляров» пути возврата не создаёт.
        /// </summary>
        private bool CanRestoreHiddenWindow =>
            (_trayIconCreated && TrayIconWanted && !Services.LinuxDesktopEnvironment.TrayMayBeUnavailable)
            || App.SingleInstanceActive;

        /// <summary>Нужен ли значок по настройкам: сам значок либо закрытие в трей.</summary>
        private bool TrayIconWanted => _vm is null || _vm.ShowTrayIcon || _vm.CloseToTray;

        /// <summary>Кэш статусов служб для меню трея (issue #11, 1.0.0.14): без
        /// блокирующего опроса systemctl при пересборке меню.</summary>
        private Services.ServiceStatusCache? _serviceStatusCache;

        /// <summary>Фоновое обновление кэша статусов служб.</summary>
        private System.Threading.Timer? _servicesStatusTimer;

        /// <summary>
        /// Уводит окно в трей после успешного запуска, как это делает WPF-версия
        /// для обоих значений настройки. Выполняется через диспетчер: запрос
        /// приходит из обработчика команды, а окно к этому моменту ещё показывает
        /// нажатую кнопку.
        /// </summary>
        private void OnAfterLaunchRequested(Models.AfterLaunchAction action)
        {
            if (action == Models.AfterLaunchAction.None)
                return;

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                // «Свернуть» — просто свернуть окно в панель задач, не уводя его в трей (issue #201).
                if (action == Models.AfterLaunchAction.Minimize)
                {
                    WindowState = WindowState.Minimized;
                    return;
                }

                // Спрятанное окно живёт только в трее, поэтому без пути возврата
                // оно сворачивается: иначе пользователь остался бы с работающим
                // процессом, который нечем показать.
                if (!CanRestoreHiddenWindow)
                {
                    WindowState = WindowState.Minimized;
                    _vm?.LogWarning(LocalizationManager.T("Main.AfterLaunchTrayUnavailable"));
                    return;
                }

                SaveWindowLayout();
                Hide();
                if (action == Models.AfterLaunchAction.MinimizeToTray
                    && WindowState == WindowState.Minimized)
                    WindowState = WindowState.Normal;
            });
        }

        /// <summary>
        /// Строит меню трея с актуальными переводами. Вызывается при создании
        /// значка и при смене языка, чтобы подписи и подсказки обновились.
        /// </summary>
        private NativeMenu BuildTrayMenu()
        {
            var menu = new NativeMenu();
            // Состав меню зависит от данных: недавние базы и выбранная база
            // меняются в работе. Штатный запрос обновления перед показом
            // (NeedsUpdate) на Linux не приходит: его поднимает только
            // бэкенд macOS, в Avalonia.FreeDesktop вызова нет, проверено
            // и прогоном, и разбором сборок. Поэтому меню пересобирается
            // по изменению самих данных, а подписка оставлена на случай,
            // если событие появится.
            menu.NeedsUpdate += (_, _) => FillTrayMenu(menu);
            FillTrayMenu(menu);
            _trayMenu = menu;
            _traySignature = TrayMenuSignature();
            return menu;
        }

        /// <summary>
        /// Ставит пересборку меню трея в очередь диспетчера. Одно действие даёт
        /// несколько уведомлений подряд (выбор, список, запуск), а пересборка
        /// заново экспортирует меню по DBus, поэтому вызовы склеиваются, как
        /// это уже делают QueueHeaderAlign и QueueColumnHeaderRefresh.
        /// </summary>
        private void QueueTrayMenuRefresh()
        {
            if (_trayRefreshQueued || _trayMenu is null || _vm is null)
                return;

            _trayRefreshQueued = true;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _trayRefreshQueued = false;
                RefreshTrayMenu();
            }, Avalonia.Threading.DispatcherPriority.Background);
        }

        /// <summary>
        /// Пересобирает меню трея, если его состав действительно изменился:
        /// уведомления приходят и на поиск, и на фильтр, поэтому сверяется
        /// отпечаток состава.
        /// </summary>
        private void RefreshTrayMenu()
        {
            if (_trayMenu is null || _vm is null)
                return;

            var signature = TrayMenuSignature();
            if (signature == _traySignature)
                return;

            _traySignature = signature;
            FillTrayMenu(_trayMenu);
        }

        /// <summary>Состав меню трея строкой: выбранная база и недавние.</summary>
        private string TrayMenuSignature()
        {
            if (_vm is null)
                return string.Empty;

            var builder = new System.Text.StringBuilder();
            builder.Append(_vm.SelectedInfobase?.Id).Append('|').Append(_vm.SelectedInfobase?.Name);
            foreach (var (ib, _) in _vm.GetFavoriteInfobasesOrdered())
                builder.Append('|').Append('F').Append(ib.Id).Append('~').Append(ib.Name);
            foreach (var ib in _vm.RecentInfobases)
                builder.Append('|').Append(ib.Id).Append('~').Append(ib.Name);
            return builder.ToString();
        }

        /// <summary>Наполняет меню трея заново по текущему состоянию списка баз.</summary>
        private void FillTrayMenu(NativeMenu menu)
        {
            menu.Items.Clear();

            // Состав и порядок как в Windows-версии (MainWindow.Tray.cs:209):
            // открыть, избранные базы (закладки), недавние базы (или выбранная,
            // если недавних нет), синхронизация, настройки, выход. У каждой базы
            // своё подменю «Предприятие / Конфигуратор»: раньше пункт запускал
            // только Предприятие, а выбора не было.
            var showItem = new NativeMenuItem(LocalizationManager.T("Main.TrayOpen"));
            showItem.Click += (_, _) => ShowAndActivate();
            menu.Add(showItem);

            // Избранные базы (закладки Alt+1…9): быстрый запуск без открытия окна.
            var favorites = _vm?.GetFavoriteInfobasesOrdered();
            if (favorites is { Count: > 0 })
            {
                menu.Add(new NativeMenuItemSeparator());
                menu.Add(TrayHeader(LocalizationManager.T("Main.FavoriteBases")));
                foreach (var (ib, slot) in favorites)
                    menu.Add(TrayInfobaseItem(ib, $"{slot}. {TrayItemName(ib.Name, "Main.NoName")}"));
            }

            var recent = _vm?.RecentInfobases;
            if (recent is { Count: > 0 })
            {
                menu.Add(new NativeMenuItemSeparator());
                menu.Add(TrayHeader(LocalizationManager.T("Main.RecentBases")));
                foreach (var ib in recent)
                    menu.Add(TrayInfobaseItem(ib, TrayItemName(ib.Name, "Main.NoName")));
            }
            else if (_vm?.SelectedInfobase is { } sel)
            {
                menu.Add(new NativeMenuItemSeparator());
                menu.Add(TrayHeader(LocalizationManager.T("Main.SelectedBase")));
                // У выбранной базы сам пункт ничего не запускает, только раскрывает
                // подменю, и её имя не обрезается: так у автора
                // (MainWindow.Tray.cs:240).
                var selName = string.IsNullOrWhiteSpace(sel.Name)
                    ? LocalizationManager.T("Main.SelectedBaseNoName")
                    : sel.Name;
                menu.Add(TrayInfobaseItem(sel, selName, launchOnClick: false));
            }

            // Разделитель после блока баз/избранного.
            menu.Add(new NativeMenuItemSeparator());

            // Подменю «Службы» (issue #11, итерация 2 — группы служб, релиз 1.0.0.12;
            // требования 7OH к issue #11 от 13:30/13:47). Пункт «Синхронизировать базы»
            // убран из трея (issue #62): разделитель ПОСЛЕ подменю остаётся один —
            // он отделяет «Службы» от «Настроек».
            if (BuildServicesNativeItem() is { } servicesItem)
            {
                menu.Add(servicesItem);
                menu.Add(new NativeMenuItemSeparator());
            }

            var settings = new NativeMenuItem(LocalizationManager.T("Main.Settings"));
            settings.Click += (_, _) => _vm?.OpenSettingsCommand.Execute(null);
            menu.Add(settings);
            menu.Add(new NativeMenuItemSeparator());

            // Выход: разрешаем реальное закрытие и завершаем приложение.
            var exitItem = new NativeMenuItem(LocalizationManager.T("Main.Exit"));
            exitItem.Click += (_, _) =>
            {
                _allowCloseToTray = false;
                _vm?.ExitCommand.Execute(null);
            };
            menu.Add(exitItem);
        }

        // =====================================================================
        // Подменю «Службы» (issue #11, итерация 2 — группы служб, релиз 1.0.0.12;
        // комментарии 7OH к issue #11 от 13:30/13:47). Нативные меню Avalonia
        // строятся заранее и не поддерживают цветные иконки/кружки: статус
        // группы отражается текстом в скобках (допустимое решение), а команды
        // доступны по ServiceActionAvailability текущего статуса.
        // =====================================================================

        /// <summary>
        /// Пункт «Службы» системного меню: «Настроить…», группы/службы списка и
        /// «Все сервисы» (адаптер модели <see cref="ServiceTrayMenuBuilder"/> к
        /// нативному меню). Статусы служб опрашиваются при каждом пересборе меню
        /// (блокирующие systemctl-вызовы); нативные меню трея не пересобираются
        /// при показе (NeedsUpdate поднимает только macOS-бэкенд — см. комментарий
        /// в BuildTrayMenu), поэтому актуальность обеспечивает RefreshTrayMenu.
        /// </summary>
        private NativeMenuItem? BuildServicesNativeItem()
        {
            try
            {
                var services = AppServices.TryGetService<IServiceControllerService>();
                var store = AppServices.TryGetService<IServicesStore>();
                if (services is null || store is null)
                    return null;

                var data = store.Load();
                // Статусы из кэша (мгновенно), обновление — фоном, UI не блокируется.
                EnsureServicesStatusTimer();
                RefreshServicesStatusesInBackground();
                var items = new ServiceTrayMenuBuilder().Build(
                    data,
                    name => ServicesStatusCache()?.GetStatus(name).Status ?? ServiceStatus.Unknown,
                    ServicePlatform.Linux);

                var item = new NativeMenuItem(LocalizationManager.T("Tray.Services"));
                var menu = new NativeMenu();
                foreach (var mi in items)
                    AppendNativeServiceItem(mi, menu);
                item.Menu = menu;
                return item;
            }
            catch
            {
                // Меню не должно падать из-за недоступности служб (нет systemd и т.п.).
                return null;
            }
        }

        /// <summary>Адаптер пункта модели меню служб к нативному меню Avalonia.</summary>
        private void AppendNativeServiceItem(ServiceMenuItem item, NativeMenu owner)
        {
            switch (item.Kind)
            {
                case ServiceMenuItemKind.Separator:
                    owner.Add(new NativeMenuItemSeparator());
                    break;

                case ServiceMenuItemKind.Settings:
                {
                    // «Настроить…» — окно «Управление службами» (требование 7OH).
                    var settings = new NativeMenuItem(item.Text);
                    settings.Click += (_, _) => _vm?.ShowServicesCommand.Execute(null);
                    owner.Add(settings);
                    break;
                }

                case ServiceMenuItemKind.ServiceSubmenu:
                case ServiceMenuItemKind.GroupSubmenu:
                case ServiceMenuItemKind.AllServicesSubmenu:
                {
                    // NativeMenuItem не имеет иконок-цветов: для ГРУППЫ статус
                    // добавляется в подпись — «Имя группы (Частично)» (допустимое
                    // решение для Avalonia; статус из ServiceGroupStatusResolver).
                    var text = item.Kind == ServiceMenuItemKind.GroupSubmenu
                              && !string.IsNullOrEmpty(item.StatusText)
                        ? $"{item.Text} ({item.StatusText})"
                        : item.Text;
                    var node = new NativeMenuItem(text);
                    var submenu = new NativeMenu();
                    foreach (var child in item.Children)
                        AppendNativeServiceItem(child, submenu);
                    node.Menu = submenu;
                    owner.Add(node);
                    break;
                }

                case ServiceMenuItemKind.ServiceCommand:
                case ServiceMenuItemKind.GroupCommand:
                case ServiceMenuItemKind.AllServicesCommand:
                {
                    var command = new NativeMenuItem(item.Text) { IsEnabled = item.IsEnabled };
                    var action = item.Action ?? ServiceAction.Start;
                    var target = item.Target;
                    var targetName = item.TargetName;
                    var names = item.ServiceNames.ToList();
                    command.Click += (_, _) => ExecuteTrayServiceAction(action, target, targetName, names);
                    owner.Add(command);
                    break;
                }

                default:
                    owner.Add(new NativeMenuItem(item.Text) { IsEnabled = item.IsEnabled });
                    break;
            }
        }

        /// <summary>
        /// Выполняет операцию над целевыми службами (отдельная служба / члены группы /
        /// все службы списка): systemctl-команды блокирующие, поэтому выполнение — в
        /// фоновом потоке; после завершения меню трея пересобирается (статусы изменились).
        /// </summary>
        private void ExecuteTrayServiceAction(
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
                        // Статусы актуализируем на момент операции (как в окне
                        // «Управление службами»): доступность могла измениться.
                        var info = services.GetStatus(name);
                        if (!ServiceActionAvailability.CanExecute(
                                action, info.Status, info.CanPauseAndContinue, info.CanStop, ServicePlatform.Linux))
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
                        // Отдельный unit не должен останавливать остальные.
                    }
                }
            });

            // Меню пересобираем после операции: сигнатура БАЗ не изменилась, поэтому
            // RefreshTrayMenu сам не сработает — форсируем полную пересборку.
            Avalonia.Threading.Dispatcher.UIThread.Post(RefreshTrayServicesMenu, Avalonia.Threading.DispatcherPriority.Background);
        }

        /// <summary>Пересобирает меню трея с актуальными статусами служб.</summary>
        private void RefreshTrayServicesMenu()
        {
            if (_trayMenu is null)
                return;
            // Статусы изменились — кэш сбрасываем и опрашиваем заново в фоне.
            _serviceStatusCache?.Invalidate();
            RefreshServicesStatusesInBackground();
            // Статусы служб не входят в сигнатуру баз: сбрасываем отпечаток, чтобы
            // RefreshTrayMenu не отбросил пересборку по неизменённым базам.
            _traySignature = null;
            FillTrayMenu(_trayMenu);
        }

        /// <summary>Единый кэш статусов из DI (issue #3, часть 2): трей, главное окно
        /// и меню «СЛУЖБЫ» монитора серверов читают статусы из одного экземпляра.</summary>
        private Services.ServiceStatusCache? ServicesStatusCache()
        {
            if (_serviceStatusCache is null)
                _serviceStatusCache = AppServices.TryGetService<Services.ServiceStatusCache>();
            return _serviceStatusCache;
        }

        /// <summary>Запускает фоновый таймер обновления кэша статусов служб (5 с).</summary>
        private void EnsureServicesStatusTimer()
        {
            if (_servicesStatusTimer is not null)
                return;
            _servicesStatusTimer = new System.Threading.Timer(
                _ => RefreshServicesStatusesInBackground(),
                null,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(5));
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
        /// Пункты подменю «Службы» по данным services.json и статусам из кэша
        /// (issue #11, 1.0.0.14): построение не блокирует UI — статусы читаются
        /// из <see cref="ServiceStatusCache"/> и обновляются фоном. Используется
        /// и меню трея, и контекстным меню служб у курсора (issue #11, пункт 6).
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
                return builder.Build(data, _ => new ServiceInfo { Status = ServiceStatus.Unknown }, ServicePlatform.Linux);
            }
            // Мгновенный показ: статусы из кэша (фоновый таймер держит их свежими).
            EnsureServicesStatusTimer();
            RefreshServicesStatusesInBackground();
            return builder.Build(data,
                name => ServicesStatusCache()?.GetStatus(name).Status ?? ServiceStatus.Unknown,
                ServicePlatform.Linux);
        }

        /// <summary>
        /// Заголовок раздела в меню трея. В Windows это отдельный нерабочий
        /// пункт (MainWindow.Tray.cs:216), здесь он же, но недоступный:
        /// собственного вида у заголовка в системном меню нет.
        /// </summary>
        private static NativeMenuItem TrayHeader(string text) => new(text) { IsEnabled = false };

        /// <summary>
        /// Подпись базы в меню трея: пустое имя заменяется на подпись автора,
        /// длинное обрезается до 48 знаков, как в Windows-версии
        /// (MainWindow.Tray.cs:224).
        /// </summary>
        private static string TrayItemName(string? name, string emptyKey)
        {
            if (string.IsNullOrWhiteSpace(name))
                return LocalizationManager.T(emptyKey);
            return name.Length > 48 ? name.Substring(0, 45) + "…" : name;
        }

        /// <summary>
        /// Пункт базы с подменю выбора режима запуска: сам пункт запускает
        /// Предприятие, подменю даёт Предприятие и Конфигуратор
        /// (MainWindow.Tray.cs:271).
        /// </summary>
        private NativeMenuItem TrayInfobaseItem(Infobase ib, string title, bool launchOnClick = true)
        {
            var baseRef = ib;
            var item = new NativeMenuItem(title);
            if (launchOnClick)
                item.Click += (_, _) => LaunchInfobase(baseRef, configurator: false);

            var submenu = new NativeMenu();
            var enterprise = new NativeMenuItem(LocalizationManager.T("Main.Enterprise"));
            enterprise.Click += (_, _) => LaunchInfobase(baseRef, configurator: false);
            submenu.Add(enterprise);

            var configurator = new NativeMenuItem(LocalizationManager.T("Main.SectionConfigurator"));
            configurator.Click += (_, _) => LaunchInfobase(baseRef, configurator: true);
            submenu.Add(configurator);

            item.Menu = submenu;
            return item;
        }

        private void SetupTray()
        {
            try
            {
                var menu = BuildTrayMenu();

                var tray = new TrayIcon
                {
                    Icon = LoadTrayIcon(),
                    ToolTipText = LocalizationManager.T("App.Title"),
                    Menu = menu
                };
                // Одиночный клик левой кнопкой по иконке трея показывает и
                // фокусирует главное окно, как в WPF-версии (issue #224).
                // Правый клик открывает меню (Menu выше), левый — нет.
                tray.Clicked += (_, _) => ShowAndActivate();
                _trayIcon = tray;
                if (Application.Current is { } app)
                {
                    TrayIcon.SetIcons(app, new TrayIcons { tray });
                    _trayIconCreated = true;
                }

                ApplyTrayVisibility();

                // На GNOME Shell без расширения AppIndicator иконка трея не появится,
                // и приложение об этом никак не узнает: ошибки не будет, значка просто
                // не будет. Пишем в журнал, чтобы это не выглядело поломкой приложения.
                if (Services.LinuxDesktopEnvironment.TrayMayBeUnavailable)
                {
                    AppServices.GetRequiredService<Services.IAppLogger>().Warn(
                        $"Окружение {Services.LinuxDesktopEnvironment.Describe()}: " +
                        "иконка в трее может не отображаться без расширения AppIndicator.");
                }
            }
            catch
            {
                // Трей не обязателен для работы окна; игнорируем ошибки инициализации.
                // Примечание: на GNOME Shell без AppIndicator трей Avalonia может не отображаться —
                // это ограничение DE, окно продолжает работать обычным образом.
            }
        }

        /// <summary>Запускает базу из меню трея (Предприятие).</summary>
        private void LaunchInfobase(Infobase ib, bool configurator = false)
        {
            if (_vm is null)
                return;
            // Пункт меню держит ссылку на базу, а список мог смениться
            // импортом или удалением: запускать исчезнувшую нельзя.
            if (!_vm.Infobases.Contains(ib))
            {
                QueueTrayMenuRefresh();
                return;
            }
            _vm.LaunchFromTray(ib, configurator);
        }

        /// <summary>
        /// Загружает иконку трея — тот же значок приложения (app.ico), что и у
        /// заголовка окна. Без System.Drawing, через Avalonia WindowIcon.
        /// </summary>
        private static WindowIcon? LoadTrayIcon() => Services.AppIconLoader.LoadAppIcon();

        /// <summary>
        /// Показывает или прячет значок по настройкам. Значок нужен и когда сам
        /// он выключен, но закрытие уводит окно в трей: иначе окно нечем вернуть.
        /// </summary>
        private void ApplyTrayVisibility()
        {
            if (_trayIcon is null)
            {
                // Значок не создался при загрузке: пробуем ещё раз, иначе
                // включение настройки не даст ничего до перезапуска.
                if (TrayIconWanted)
                    SetupTray();
                return;
            }

            var wanted = TrayIconWanted;

            // Пока окно спрятано, значок обязан оставаться видимым: он
            // единственный надёжный путь назад. Если настройки требуют его
            // убрать, сперва возвращаем окно.
            if (!wanted && !IsVisible)
                ShowAndActivate();

            _trayIcon.IsVisible = wanted;
        }

        /// <summary>Позволяет повторно показать окно из трея/активации.</summary>
        public void ShowAndActivate()
        {
            if (!IsVisible)
                Show();
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();
            // Блокировка приложения (issue #294): попытка открыть окно из трея
            // сопровождается запросом пароля; без верного пароля окно снова спрячется
            // (обработчик AppLockPromptDismissed).
            if (_vm is { IsAppLocked: true })
                _vm.ShowAppUnlockDialog();
        }

        /// <summary>
        /// Блокировка включена (issue #294): окно прячется в трей, чтобы не мешать
        /// работать; без пути возврата (значка в трее) — сворачивается. Пароль будет
        /// запрошен при попытке открыть окно из трея или после перезапуска.
        /// </summary>
        private void HideForAppLock()
        {
            if (CanRestoreHiddenWindow)
            {
                SaveWindowLayout();
                // Значок в трее обязан быть виден: пока блокировка активна, он —
                // основной путь назад к окну ввода пароля.
                if (_trayIcon is not null)
                    _trayIcon.IsVisible = true;
                Hide();
            }
            else
            {
                WindowState = WindowState.Minimized;
            }
        }

        /// <summary>Блокировка включена: окно прячется (в трей или свернуто).</summary>
        private void OnViewModelAppLockEngaged(object? sender, EventArgs e) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(HideForAppLock);

        /// <summary>
        /// Окно ввода пароля закрыто без верного пароля (issue #294): окно приложения
        /// прячется снова, блокировка остаётся активной.
        /// </summary>
        private void OnViewModelAppLockPromptDismissed(object? sender, EventArgs e)
        {
            if (_vm is { IsAppLocked: true })
                Avalonia.Threading.Dispatcher.UIThread.Post(HideForAppLock);
        }
    }
}
#endif