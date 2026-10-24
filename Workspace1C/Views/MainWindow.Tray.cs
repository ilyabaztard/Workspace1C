#if WINDOWS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using MaterialDesignThemes.Wpf;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Themes;
using Workspace1C.ViewModels;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using Point = System.Windows.Point;

namespace Workspace1C
{
    public partial class MainWindow
    {

        /// <summary>
        /// Текущий экземпляр NotifyIcon системного трея — точка доступа для системных
        /// уведомлений (функция №4): balloon-tip показывается именно через этот значок,
        /// без AUMID/WinRT. Обновляется при (пере)создании и освобождении значка;
        /// null, если трей недоступен.
        /// </summary>
        internal static Forms.NotifyIcon? TrayIconInstance { get; private set; }

        /// <summary>Кэш статусов служб для меню трея (issue #11, 1.0.0.14): мгновенный
        /// Ctrl+ПКМ без синхронного опроса статусов на UI-потоке.</summary>
        private Services.ServiceStatusCache? _serviceStatusCache;

        /// <summary>Фоновое обновление кэша статусов служб (5 с).</summary>
        private System.Threading.Timer? _servicesStatusTimer;

        private static readonly TimeSpan ServicesStatusRefreshInterval = TimeSpan.FromSeconds(5);

        /// <summary>Размер иконок пунктов меню трея (issue #40, 1.0.0.45): 20×20 — заметно
        /// крупнее прежних 16×16, кружочки статусов служб/групп читаются лучше.</summary>
        private const int TrayMenuIconSize = 20;

        private void InitializeTrayIcon()
        {
            try
            {
                // Старый экземпляр (повторный вызов) — освобождаем
                if (_trayIcon != null)
                {
                    try { _trayIcon.Visible = false; _trayIcon.Dispose(); } catch { /* ignore */ }
                    _trayIcon = null;
                }

                // Важно: Icon задать ДО Visible — иначе на Win10/11 иконка в трее может не появиться
                var icon = LoadApplicationIcon() ?? Drawing.SystemIcons.Application;

                _trayIcon = new Forms.NotifyIcon
                {
                    Text = LocalizationManager.T("App.Title"),
                    Icon = icon,
                    Visible = false
                };
                TrayIconInstance = _trayIcon;

                try
                {
                    // Обычное меню трея: пересборка при каждом показе (Opening) и
                    // страховка Ctrl+ПКМ (меню служб открыто — штатное не показываем).
                    var menu = CreateMainTrayMenu();
                    _trayIcon.ContextMenuStrip = menu;
                }
                catch
                {
                    // Меню не критично — иконка всё равно должна быть
                }

                _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
                _trayIcon.MouseClick += (_, e) =>
                {
                    if (e.Button == Forms.MouseButtons.Left)
                        RestoreFromTray();
                };
                // Ctrl+ПКМ (issue #11, 7OH: «если получится»): сразу меню управления службами.
                // На MouseDown (до старта системного показа) штатный ContextMenuStrip
                // подменяется менем служб, собранным из прогретого кэша — системный показ
                // NotifyIcon открывает его как обычное меню (закрытие по клику вне меню
                // и ESC работает нативно, замечание 7OH от 19.10 к issue #11).
                _trayIcon.MouseDown += OnTrayIconMouseDown;
                _trayIcon.MouseUp += OnTrayIconMouseUp;

                // Показ: по настройке «Показывать в трее» или всегда, если закрытие в трей
                _trayIcon.Visible = _viewModel.ShowTrayIcon || _viewModel.CloseToTray;

                // Прогрев кэша статусов служб (issue #11, 1.0.0.18): первый Ctrl+ПКМ
                // сразу после запуска открывает меню служб мгновенно — статусы уже
                // в кэше (раньше первые ~секунду кэш был пуст, «держать около секунды»).
                EnsureServicesStatusTimer();
                RefreshServicesStatusesInBackground();
            }
            catch
            {
                // Последняя попытка — минимальный NotifyIcon без меню
                try
                {
                    _trayIcon = new Forms.NotifyIcon
                    {
                        Text = LocalizationManager.T("App.Title"),
                        Icon = Drawing.SystemIcons.Application,
                        Visible = true
                    };
                    TrayIconInstance = _trayIcon;
                    _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
                }
                catch
                {
                    _trayIcon = null;
                }
            }
        }

        /// <summary>
        /// Загрузка иконки приложения для трея (16×16 предпочтительно).
        /// Работает при PublishSingleFile.
        /// </summary>
        private static Drawing.Icon? LoadApplicationIcon()
        {
            // 1) Embedded resource «app.ico» (приоритет), затем «tray.ico»
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                string[] preferred = ["app.ico", "tray.ico"];
                var names = asm.GetManifestResourceNames();
                foreach (var pref in preferred)
                {
                    foreach (var name in names)
                    {
                        if (!name.EndsWith(pref, StringComparison.OrdinalIgnoreCase) &&
                            !name.Equals(pref, StringComparison.OrdinalIgnoreCase))
                            continue;
                        using var stream = asm.GetManifestResourceStream(name);
                        if (stream is null) continue;
                        return CreateTraySizedIcon(stream);
                    }
                }
            }
            catch { /* ignore */ }

            // 2) WPF Resource (pack URI): app.ico, затем tray.ico
            try
            {
                foreach (var res in new[] { "app.ico", "tray.ico" })
                {
                    var uri = new Uri($"pack://application:,,,/{res}", UriKind.Absolute);
                    var info = Application.GetResourceStream(uri);
                    if (info?.Stream is null) continue;
                    using (info.Stream)
                        return CreateTraySizedIcon(info.Stream);
                }
            }
            catch { /* ignore */ }

            // 3) Файл рядом с exe / BaseDirectory
            try
            {
                var dirs = new List<string>();
                if (!string.IsNullOrEmpty(AppDomain.CurrentDomain.BaseDirectory))
                    dirs.Add(AppDomain.CurrentDomain.BaseDirectory);
                var proc = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(proc))
                {
                    var d = System.IO.Path.GetDirectoryName(proc);
                    if (!string.IsNullOrEmpty(d)) dirs.Add(d);
                }
                foreach (var dir in dirs.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    foreach (var fileName in new[] { "app.ico", "tray.ico" })
                    {
                        var iconPath = System.IO.Path.Combine(dir, fileName);
                        if (!System.IO.File.Exists(iconPath)) continue;
                        using var fs = System.IO.File.OpenRead(iconPath);
                        return CreateTraySizedIcon(fs);
                    }
                }
            }
            catch { /* ignore */ }

            // 4) Иконка, вшитая в exe (ApplicationIcon)
            try
            {
                var exePath = Environment.ProcessPath
                    ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(exePath) && System.IO.File.Exists(exePath))
                {
                    var extracted = Drawing.Icon.ExtractAssociatedIcon(exePath);
                    if (extracted is not null)
                        return extracted;
                }
            }
            catch { /* ignore */ }

            return null;
        }

        /// <summary>Иконка 16×16 для трея (лучше отображается в notification area).</summary>
        private static Drawing.Icon CreateTraySizedIcon(System.IO.Stream stream)
        {
            using var source = new Drawing.Icon(stream);
            try
            {
                using var sized = new Drawing.Icon(source, 16, 16);
                return (Drawing.Icon)sized.Clone();
            }
            catch
            {
                return (Drawing.Icon)source.Clone();
            }
        }

        /// <summary>Создаёт ContextMenuStrip в современном стиле (рендерер, шрифт, отступы).</summary>
        private static Forms.ContextMenuStrip CreateModernTrayMenu()
        {
            var menu = new Forms.ContextMenuStrip
            {
                Font = new Drawing.Font("Segoe UI", 9.5f, Drawing.FontStyle.Regular),
                ShowImageMargin = true,
                ShowCheckMargin = false,
                ImageScalingSize = new Drawing.Size(TrayMenuIconSize, TrayMenuIconSize),
                Padding = new Forms.Padding(2, 4, 2, 4),
                Renderer = new ModernTrayMenuRenderer()
            };
            return menu;
        }

        /// <summary>
        /// Собирает меню трея по эффективной компоновке (issue #62): фиксированный пункт
        /// «Открыть программу», затем блоки компоновки (Избранные/Недавние/Службы/группы)
        /// со стилем «Блок меню» (заголовок + содержимое) или «Подменю», в конце —
        /// разделитель, «Настройки», разделитель, «Выход». Структуру строит чистый
        /// <see cref="TrayMenuBuilder"/>, адаптер добавляет динамическое содержимое
        /// блоков (базы, службы).
        /// </summary>
        private void RebuildTrayMenu(Forms.ContextMenuStrip menu)
        {
            menu.Items.Clear();

            var store = AppServices.TryGetService<ITrayMenuLayoutStore>();
            var settings = new AppSettings();
            settings.TrayMenuEditingEnabled = _viewModel?.TrayMenuEditingEnabled ?? false;
            var layout = TrayMenuResolver.Resolve(settings, store);
            var items = new TrayMenuBuilder().Build(layout);

            foreach (var item in items)
                AppendTrayItem(menu, item);
        }

        /// <summary>Пересборка меню трея сразу (например после закрытия окна настроек,
        /// когда изменилась настройка «Правка структуры меню в трее» — issue #62).</summary>
        public void RebuildTrayMenuNow()
        {
            if (_trayIcon?.ContextMenuStrip is { } menu)
                RebuildTrayMenu(menu);
        }

        /// <summary>Адаптер пункта чистой модели <see cref="TrayMenuItem"/> к WinForms-меню.</summary>
        private void AppendTrayItem(Forms.ContextMenuStrip menu, TrayMenuItem item)
        {
            switch (item.Kind)
            {
                case TrayItemKind.Open:
                    menu.Items.Add(CreateTrayItem(item.Text, TrayIconKind.Open, (_, _) => RestoreFromTray()));
                    break;

                case TrayItemKind.Separator:
                    menu.Items.Add(new Forms.ToolStripSeparator());
                    break;

                case TrayItemKind.Header:
                    AppendTrayBlock(menu, item);
                    break;

                case TrayItemKind.Submenu:
                    AppendTraySubmenu(menu, item);
                    break;

                case TrayItemKind.Settings:
                    menu.Items.Add(CreateTrayItem(item.Text, TrayIconKind.Settings, (_, _) =>
                    {
                        RestoreFromTray();
                        if (_viewModel.OpenSettingsCommand.CanExecute(null))
                            _viewModel.OpenSettingsCommand.Execute(null);
                    }));
                    break;

                case TrayItemKind.Exit:
                    menu.Items.Add(CreateTrayItem(item.Text, TrayIconKind.Exit, (_, _) =>
                    {
                        _forceClose = true;
                        Close();
                    }));
                    break;

                default:
                    menu.Items.Add(CreateTrayItem(item.Text, TrayIconKind.Group, null));
                    break;
            }
        }

        /// <summary>Блок в стиле «Блок меню»: текст-заголовок, под ним динамическое
        /// содержимое (как сейчас — избранные/недавние; для служб — плоские пункты
        /// меню служб; для пользовательских групп — дети компоновки).</summary>
        private void AppendTrayBlock(Forms.ContextMenuStrip menu, TrayMenuItem item)
        {
            if (item.Block == TrayMenuBlockKind.Favorites)
            {
                // Избранные базы (закладки Alt+1…9): быстрый запуск без открытия окна.
                var favorites = _viewModel.GetFavoriteInfobasesOrdered();
                if (favorites.Count == 0)
                    return;
                AddTrayBlockStart(menu);
                menu.Items.Add(CreateTrayHeader(item.Text));
                foreach (var (ib, slot) in favorites)
                {
                    var name = string.IsNullOrWhiteSpace(ib.Name) ? LocalizationManager.T("Main.NoName") : ib.Name;
                    if (name.Length > 48)
                        name = name.Substring(0, 45) + "…";

                    var id = ib.Id;
                    var favoriteItem = CreateTrayItem($"{slot}. {name}", TrayIconKind.Favorite, (_, _) =>
                        _viewModel.LaunchInfobaseById(id, isConfigurator: false));
                    AttachLaunchSubmenu(favoriteItem, menu, id);
                    menu.Items.Add(favoriteItem);
                }
                return;
            }

            if (item.Block == TrayMenuBlockKind.Recent)
            {
                // Недавние базы (по дате последнего запуска) или выбранная база.
                var recent = _viewModel.GetRecentInfobases(7).ToList();
                if (recent.Count > 0)
                {
                    AddTrayBlockStart(menu);
                    menu.Items.Add(CreateTrayHeader(item.Text));
                    foreach (var ib in recent)
                    {
                        var name = string.IsNullOrWhiteSpace(ib.Name) ? LocalizationManager.T("Main.NoName") : ib.Name;
                        if (name.Length > 48)
                            name = name.Substring(0, 45) + "…";

                        var id = ib.Id;
                        var baseItem = CreateTrayItem(name, TrayIconKind.Database, (_, _) =>
                            _viewModel.LaunchInfobaseById(id, isConfigurator: false));
                        AttachLaunchSubmenu(baseItem, menu, id);
                        menu.Items.Add(baseItem);
                    }
                }
                else if (_viewModel.SelectedInfobase != null)
                {
                    AddTrayBlockStart(menu);
                    menu.Items.Add(CreateTrayHeader(item.Text));
                    var selName = _viewModel.SelectedInfobase.Name;
                    if (string.IsNullOrWhiteSpace(selName)) selName = LocalizationManager.T("Main.SelectedBaseNoName");
                    var selId = _viewModel.SelectedInfobase.Id;
                    var selItem = CreateTrayItem(selName, TrayIconKind.Database, null);
                    AttachLaunchSubmenu(selItem, menu, selId);
                    menu.Items.Add(selItem);
                }
                return;
            }

            if (item.Block == TrayMenuBlockKind.Services)
            {
                // Блок «Службы» в стиле «Блок меню»: заголовок, под ним плоские пункты
                // меню служб (Настроить…, службы/группы, Все сервисы) — issue #62.
                AddTrayBlockStart(menu);
                menu.Items.Add(CreateTrayHeader(item.Text));
                foreach (var mi in BuildServicesMenuItems())
                    menu.Items.Add(ToServiceTrayItem(mi));
                return;
            }

            // Пользовательская группа: заголовок + дети компоновки (зарезервировано).
            AddTrayBlockStart(menu);
            menu.Items.Add(CreateTrayHeader(item.Text));
            AppendChildItems(menu.Items, item);
        }

        /// <summary>Блок (или пользовательская группа) в стиле «Подменю»: пункт с вложенным меню.
        /// Для Служб — текущий вид (BuildServicesSubmenu); для баз — подменю со списком.</summary>
        private void AppendTraySubmenu(Forms.ContextMenuStrip menu, TrayMenuItem item)
        {
            if (item.Block == TrayMenuBlockKind.Favorites)
            {
                var favorites = _viewModel.GetFavoriteInfobasesOrdered();
                if (favorites.Count == 0)
                    return;
                AddTrayBlockStart(menu);
                var submenu = CreateTrayItem(item.Text, TrayIconKind.Favorite, null);
                ApplyModernDropDownStyle(submenu.DropDown);
                foreach (var (ib, slot) in favorites)
                {
                    var name = string.IsNullOrWhiteSpace(ib.Name) ? LocalizationManager.T("Main.NoName") : ib.Name;
                    if (name.Length > 48)
                        name = name.Substring(0, 45) + "…";
                    var id = ib.Id;
                    var favoriteItem = CreateTrayItem($"{slot}. {name}", TrayIconKind.Favorite, (_, _) =>
                        _viewModel.LaunchInfobaseById(id, isConfigurator: false));
                    AttachLaunchSubmenu(favoriteItem, menu, id);
                    submenu.DropDownItems.Add(favoriteItem);
                }
                menu.Items.Add(submenu);
                return;
            }

            if (item.Block == TrayMenuBlockKind.Recent)
            {
                var recent = _viewModel.GetRecentInfobases(7).ToList();
                if (recent.Count > 0)
                {
                    AddTrayBlockStart(menu);
                    var submenu = CreateTrayItem(item.Text, TrayIconKind.Database, null);
                    ApplyModernDropDownStyle(submenu.DropDown);
                    foreach (var ib in recent)
                    {
                        var name = string.IsNullOrWhiteSpace(ib.Name) ? LocalizationManager.T("Main.NoName") : ib.Name;
                        if (name.Length > 48)
                            name = name.Substring(0, 45) + "…";
                        var id = ib.Id;
                        var baseItem = CreateTrayItem(name, TrayIconKind.Database, (_, _) =>
                            _viewModel.LaunchInfobaseById(id, isConfigurator: false));
                        AttachLaunchSubmenu(baseItem, menu, id);
                        submenu.DropDownItems.Add(baseItem);
                    }
                    menu.Items.Add(submenu);
                }
                return;
            }

            if (item.Block == TrayMenuBlockKind.Services)
            {
                // Стиль «Подменю» для Служб — текущий вид подменю (issue #62).
                AddTrayBlockStart(menu);
                menu.Items.Add(BuildServicesSubmenu(item.Text));
                return;
            }

            // Пользовательская группа: подменю с детьми компоновки (зарезервировано).
            AddTrayBlockStart(menu);
            var groupSubmenu = CreateTrayItem(item.Text, TrayIconKind.Group, null);
            ApplyModernDropDownStyle(groupSubmenu.DropDown);
            AppendChildItems(groupSubmenu.DropDownItems, item);
            menu.Items.Add(groupSubmenu);
        }

        /// <summary>Разделитель перед блоком, если меню ещё не пусто (первый фиксированный
        /// пункт «Открыть программу» уже добавлен адаптером — issue #62).</summary>
        private void AddTrayBlockStart(Forms.ContextMenuStrip menu)
        {
            if (menu.Items.Count > 0)
                menu.Items.Add(new Forms.ToolStripSeparator());
        }

        /// <summary>Дети пользовательской группы в подменю/блоке (зарезервировано — сейчас
        /// группы верхнего уровня; поддерживаем разделители и пункты компоновки).</summary>
        private void AppendChildItems(Forms.ToolStripItemCollection owner, TrayMenuItem item)
        {
            foreach (var child in item.Children)
            {
                if (child.IsSeparator)
                {
                    owner.Add(new Forms.ToolStripSeparator());
                    continue;
                }
                owner.Add(CreateTrayItem(child.Text, TrayIconKind.Group, null));
            }
        }

        /// <summary>
        /// Подменю «Предприятие / Конфигуратор» без зазора от родительского пункта.
        /// </summary>
        private void AttachLaunchSubmenu(Forms.ToolStripMenuItem parent, Forms.ContextMenuStrip ownerMenu, string infobaseId)
        {
            parent.DropDownItems.Add(CreateTrayItem(LocalizationManager.T("Main.Enterprise"), TrayIconKind.Enterprise, (_, _) =>
                _viewModel.LaunchInfobaseById(infobaseId, isConfigurator: false)));
            parent.DropDownItems.Add(CreateTrayItem(LocalizationManager.T("Main.SectionConfigurator"), TrayIconKind.Configurator, (_, _) =>
                _viewModel.LaunchInfobaseById(infobaseId, isConfigurator: true)));

            var dd = parent.DropDown;
            dd.Renderer = ownerMenu.Renderer;
            dd.Font = ownerMenu.Font;
            dd.ImageScalingSize = ownerMenu.ImageScalingSize;
            dd.Padding = new Forms.Padding(2, 2, 2, 2);
            dd.Margin = new Forms.Padding(0);
            if (dd is Forms.ToolStripDropDownMenu dropMenu)
            {
                dropMenu.ShowImageMargin = true;
                dropMenu.ShowCheckMargin = false;
            }

            // WinForms по умолчанию оставляет щель между меню и подменю — сдвигаем вплотную
            parent.DropDownOpened += (_, _) =>
            {
                try
                {
                    var loc = dd.Location;
                    dd.Location = new Drawing.Point(loc.X - 8, loc.Y);
                }
                catch { /* ignore */ }
            };
        }

        private static Forms.ToolStripMenuItem CreateTrayItem(string text, TrayIconKind kind, EventHandler? onClick)
        {
            var item = new Forms.ToolStripMenuItem(text)
            {
                Image = GetTrayIcon(kind),
                ImageScaling = Forms.ToolStripItemImageScaling.None,
                DisplayStyle = Forms.ToolStripItemDisplayStyle.ImageAndText,
                ImageAlign = Drawing.ContentAlignment.MiddleLeft,
                TextAlign = Drawing.ContentAlignment.MiddleLeft,
                Padding = new Forms.Padding(2, 3, 4, 3),
                AutoSize = true
            };
            if (onClick != null)
                item.Click += onClick;
            return item;
        }

        private static Forms.ToolStripLabel CreateTrayHeader(string text)
        {
            return new Forms.ToolStripLabel(text)
            {
                ForeColor = Drawing.Color.FromArgb(100, 116, 139),
                Font = new Drawing.Font("Segoe UI Semibold", 8.5f, Drawing.FontStyle.Regular),
                Padding = new Forms.Padding(6, 4, 6, 2)
            };
        }

        /// <summary>
        /// Иконки меню трея: те же символы Material Design, что PackIcon в главном окне
        /// (Play, Wrench, Database, Cog, Sync, ExitToApp, Application), 16×16.
        /// Рисуются через GDI+ по геометрии MD — без зависимости от WPF visual tree
        /// (PackIcon вне окна даёт пустой bitmap).
        /// </summary>
        private static Drawing.Image GetTrayIcon(TrayIconKind kind)
        {
            if (TrayIconCache.TryGetValue(kind, out var cached))
                return cached;

            var img = DrawMaterialTrayIcon(kind, TrayMenuIconSize);
            TrayIconCache[kind] = img;
            return img;
        }

        private static Drawing.Image DrawMaterialTrayIcon(TrayIconKind kind, int size)
        {
            var bmp = new Drawing.Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var g = Drawing.Graphics.FromImage(bmp);
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.PixelOffsetMode = Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.Clear(Drawing.Color.Transparent);

            // Цвета как у кнопок/меню главного окна
            Drawing.Color c = kind switch
            {
                TrayIconKind.Open => Drawing.Color.FromArgb(34, 197, 94),
                TrayIconKind.Database => Drawing.Color.FromArgb(59, 130, 246),
                TrayIconKind.Enterprise => Drawing.Color.FromArgb(37, 99, 235),
                TrayIconKind.Configurator => Drawing.Color.FromArgb(100, 116, 139),
                TrayIconKind.Sync => Drawing.Color.FromArgb(20, 184, 166),
                TrayIconKind.Settings => Drawing.Color.FromArgb(100, 116, 139),
                TrayIconKind.Exit => Drawing.Color.FromArgb(220, 38, 38),
                TrayIconKind.Favorite => Drawing.Color.FromArgb(245, 158, 11),
                TrayIconKind.Service => Drawing.Color.FromArgb(34, 197, 94),
                TrayIconKind.Group => Drawing.Color.FromArgb(139, 92, 246),
                _ => Drawing.Color.FromArgb(100, 116, 139)
            };

            using var brush = new Drawing.SolidBrush(c);
            using var pen = new Drawing.Pen(c, Math.Max(1.4f, size / 11f))
            {
                StartCap = Drawing.Drawing2D.LineCap.Round,
                EndCap = Drawing.Drawing2D.LineCap.Round,
                LineJoin = Drawing.Drawing2D.LineJoin.Round
            };

            float s = size;
            switch (kind)
            {
                case TrayIconKind.Enterprise:
                    // PackIcon Kind=Play — треугольник
                    g.FillPolygon(brush, new[]
                    {
                        new Drawing.PointF(s * 0.28f, s * 0.18f),
                        new Drawing.PointF(s * 0.82f, s * 0.50f),
                        new Drawing.PointF(s * 0.28f, s * 0.82f)
                    });
                    break;

                case TrayIconKind.Configurator:
                    // PackIcon Kind=Wrench
                    using (var path = new Drawing.Drawing2D.GraphicsPath())
                    {
                        // рукоять
                        path.AddLine(s * 0.22f, s * 0.78f, s * 0.55f, s * 0.45f);
                        using var thick = new Drawing.Pen(c, s * 0.18f)
                        {
                            StartCap = Drawing.Drawing2D.LineCap.Round,
                            EndCap = Drawing.Drawing2D.LineCap.Round
                        };
                        g.DrawLine(thick, s * 0.25f, s * 0.75f, s * 0.55f, s * 0.45f);
                        // головка
                        g.DrawArc(pen, s * 0.48f, s * 0.12f, s * 0.40f, s * 0.40f, 200, 220);
                        g.FillEllipse(brush, s * 0.58f, s * 0.22f, s * 0.12f, s * 0.12f);
                    }
                    break;

                case TrayIconKind.Database:
                    // PackIcon Kind=Database — цилиндр
                    g.FillEllipse(brush, s * 0.22f, s * 0.12f, s * 0.56f, s * 0.28f);
                    g.FillRectangle(brush, s * 0.22f, s * 0.26f, s * 0.56f, s * 0.48f);
                    g.FillEllipse(brush, s * 0.22f, s * 0.60f, s * 0.56f, s * 0.28f);
                    using (var top = new Drawing.SolidBrush(Drawing.Color.FromArgb(90, 255, 255, 255)))
                        g.FillEllipse(top, s * 0.28f, s * 0.16f, s * 0.44f, s * 0.16f);
                    break;

                case TrayIconKind.Settings:
                    // PackIcon Kind=Cog
                    {
                        float cx = s * 0.5f, cy = s * 0.5f, r = s * 0.28f, tooth = s * 0.12f;
                        for (int i = 0; i < 8; i++)
                        {
                            double a = i * Math.PI / 4.0;
                            float x = cx + (float)(Math.Cos(a) * (r + tooth * 0.35));
                            float y = cy + (float)(Math.Sin(a) * (r + tooth * 0.35));
                            g.FillEllipse(brush, x - tooth * 0.55f, y - tooth * 0.55f, tooth * 1.1f, tooth * 1.1f);
                        }
                        g.FillEllipse(brush, cx - r, cy - r, r * 2, r * 2);
                        using var hole = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 255, 255, 255));
                        // вырезаем центр «дыркой» поверх — для меню фон светлый, белый ок
                        g.FillEllipse(hole, cx - r * 0.38f, cy - r * 0.38f, r * 0.76f, r * 0.76f);
                        using var ring = new Drawing.Pen(c, s * 0.08f);
                        g.DrawEllipse(ring, cx - r * 0.38f, cy - r * 0.38f, r * 0.76f, r * 0.76f);
                    }
                    break;

                case TrayIconKind.Sync:
                    // PackIcon Kind=Sync — две стрелки по кругу
                    g.DrawArc(pen, s * 0.18f, s * 0.18f, s * 0.64f, s * 0.64f, -40, 200);
                    g.DrawArc(pen, s * 0.18f, s * 0.18f, s * 0.64f, s * 0.64f, 140, 200);
                    g.FillPolygon(brush, new[]
                    {
                        new Drawing.PointF(s * 0.72f, s * 0.12f),
                        new Drawing.PointF(s * 0.92f, s * 0.32f),
                        new Drawing.PointF(s * 0.62f, s * 0.34f)
                    });
                    g.FillPolygon(brush, new[]
                    {
                        new Drawing.PointF(s * 0.28f, s * 0.88f),
                        new Drawing.PointF(s * 0.08f, s * 0.68f),
                        new Drawing.PointF(s * 0.38f, s * 0.66f)
                    });
                    break;

                case TrayIconKind.Exit:
                    // PackIcon Kind=ExitToApp
                    g.DrawRectangle(pen, s * 0.12f, s * 0.18f, s * 0.42f, s * 0.64f);
                    g.DrawLine(pen, s * 0.42f, s * 0.50f, s * 0.88f, s * 0.50f);
                    g.FillPolygon(brush, new[]
                    {
                        new Drawing.PointF(s * 0.68f, s * 0.32f),
                        new Drawing.PointF(s * 0.92f, s * 0.50f),
                        new Drawing.PointF(s * 0.68f, s * 0.68f)
                    });
                    break;

                case TrayIconKind.Favorite:
                {
                    // Пятиконечная звезда закладок (Alt+1…9): внешний радиус 0.36,
                    // внутренний — 0.382 от внешнего, вершина вверх.
                    var cx = s * 0.5f;
                    var cy = s * 0.55f;
                    var ro = s * 0.36f;
                    var ri = ro * 0.382f;
                    var pts = new Drawing.PointF[10];
                    for (var i = 0; i < 10; i++)
                    {
                        var rad = i % 2 == 0 ? ro : ri;
                        var ang = (-90 + 36 * i) * Math.PI / 180.0;
                        pts[i] = new Drawing.PointF(
                            (float)(cx + rad * Math.Cos(ang)),
                            (float)(cy + rad * Math.Sin(ang)));
                    }
                    g.FillPolygon(brush, pts);
                    break;
                }

                case TrayIconKind.Service:
                    // PackIcon Kind=Play — служба: треугольник запуска (как Enterprise).
                    g.FillPolygon(brush, new[]
                    {
                        new Drawing.PointF(s * 0.30f, s * 0.20f),
                        new Drawing.PointF(s * 0.82f, s * 0.50f),
                        new Drawing.PointF(s * 0.30f, s * 0.80f)
                    });
                    break;

                case TrayIconKind.Group:
                    // PackIcon Kind=Folder — группа служб: папка с корешком.
                    g.FillRectangle(brush, s * 0.10f, s * 0.32f, s * 0.80f, s * 0.50f);
                    g.DrawLine(pen, s * 0.10f, s * 0.36f, s * 0.42f, s * 0.36f);
                    g.FillRectangle(brush, s * 0.10f, s * 0.28f, s * 0.30f, s * 0.10f);
                    break;

                case TrayIconKind.Open:
                default:
                    // PackIcon Kind=Application — окно
                    g.DrawRectangle(pen, s * 0.16f, s * 0.18f, s * 0.68f, s * 0.64f);
                    g.DrawLine(pen, s * 0.16f, s * 0.36f, s * 0.84f, s * 0.36f);
                    g.FillEllipse(brush, s * 0.24f, s * 0.24f, s * 0.10f, s * 0.10f);
                    break;
            }

            return bmp;
        }

        private void MinimizeToTray()
        {
            if (_trayIcon == null)
                InitializeTrayIcon();
            if (_trayIcon != null)
                _trayIcon.Visible = true;
            Hide();
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
        }

        /// <summary>
        /// Обработчик события «действие после запуска базы/конфигуратора»:
        /// сворачивает окно в трей или полностью закрывает приложение согласно
        /// глобальной настройке.
        /// </summary>
        private void OnAfterLaunchRequested(Models.AfterLaunchAction action)
        {
            if (action == Models.AfterLaunchAction.None)
                return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (action == Models.AfterLaunchAction.Minimize)
                {
                    // Просто свернуть окно, оставив его в панели задач (issue #201).
                    WindowState = WindowState.Minimized;
                }
                else if (action == Models.AfterLaunchAction.MinimizeToTray)
                    MinimizeToTray();
                else if (action == Models.AfterLaunchAction.Close)
                {
                    // «Закрыть» — полностью завершить работу приложения, не уводя его в трей.
                    _forceClose = true;
                    Close();
                }
            }));
        }

        /// <summary>Уводит главное окно в системный трей (не завершая приложение).</summary>
        private void HideToTray()
        {
            if (_trayIcon == null)
                InitializeTrayIcon();
            if (_trayIcon != null)
                _trayIcon.Visible = true;
            Hide();
        }

        private void RestoreFromTray()
        {
            Show();
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
            // Иконку в трее оставляем видимой, если она включена в настройках.
            if (_trayIcon != null && _viewModel != null)
                _trayIcon.Visible = _viewModel.ShowTrayIcon;
            // Блокировка приложения (issue #294): попытка открыть окно из трея
            // сопровождается запросом пароля; без верного пароля окно снова свернётся
            // (обработчик AppLockPromptDismissed).
            if (_viewModel is { IsAppLocked: true })
                _viewModel.ShowAppUnlockDialog();
        }

        /// <summary>
        /// Блокировка включена (issue #294): окно сворачивается в трей, чтобы не мешать
        /// работать. Пароль будет запрошен при попытке открыть окно из трея.
        /// </summary>
        private void OnViewModelAppLockEngaged(object? sender, EventArgs e) =>
            Dispatcher.BeginInvoke(new Action(MinimizeToTray));

        /// <summary>
        /// Окно ввода пароля закрыто без верного пароля (issue #294): окно приложения
        /// сворачивается в трей снова, блокировка остаётся активной.
        /// </summary>
        private void OnViewModelAppLockPromptDismissed(object? sender, EventArgs e)
        {
            if (_viewModel is { IsAppLocked: true })
                Dispatcher.BeginInvoke(new Action(MinimizeToTray));
        }

        /// <summary>
        /// Публичный вход для активации окна из другого экземпляра приложения
        /// (в том числе когда окно скрыто в системный трей).
        /// </summary>
        public void RestoreFromTrayPublic() => RestoreFromTray();

        private void DisposeTrayIcon()
        {
            // Останавливаем фоновое обновление статусов служб.
            _servicesStatusTimer?.Dispose();
            _servicesStatusTimer = null;
            _serviceStatusCache = null;
            _ctrlRightClickPending = false;
            _suppressedTrayMenu = null;
            _servicesTrayMenu = null;
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
                TrayIconInstance = null;
            }
        }

        private void UpdateTrayVisibility()
        {
            if (_trayIcon == null && (_viewModel.ShowTrayIcon || _viewModel.CloseToTray))
            {
                InitializeTrayIcon();
                return;
            }
            if (_trayIcon != null)
                _trayIcon.Visible = _viewModel.ShowTrayIcon || _viewModel.CloseToTray;
        }

        // =====================================================================
        // Подменю «Службы» системного трея (issue #11, итерация 2 — группы
        // служб, релиз 1.0.0.12; комментарии 7OH к issue #11 от 13:30 и 13:47).
        // Данные строителя ServiceTrayMenuBuilder адаптируются к WinForms.
        // =====================================================================

        /// <summary>Флаг: правый клик с Ctrl обрабатывается меню служб (штатное меню подавлено).</summary>
        private bool _ctrlRightClickPending;

        /// <summary>Штатное меню трея, подавленное при Ctrl+ПКМ (восстанавливается в Closed).</summary>
        private Forms.ContextMenuStrip? _suppressedTrayMenu;

        /// <summary>Открытое меню служб (страховка повторного Ctrl+ПКМ — не показывать дважды).</summary>
        private Forms.ContextMenuStrip? _servicesTrayMenu;

        /// <summary>
        /// Обычное меню трея: пересборка при каждом показе (Opening) и страховка
        /// Ctrl+ПКМ — если меню служб уже открыто, штатное меню не показываем.
        /// </summary>
        private Forms.ContextMenuStrip CreateMainTrayMenu()
        {
            var menu = CreateModernTrayMenu();
            menu.Opening += (_, e) =>
            {
                // Страховка для Ctrl+ПКМ: при открытом меню служб системный показ
                // обычного меню отменяем (основной путь — обработчик MouseUp).
                if (_ctrlRightClickPending)
                {
                    e.Cancel = true;
                    return;
                }
                RebuildTrayMenu(menu);
            };
            RebuildTrayMenu(menu);
            return menu;
        }

        /// <summary>
        /// Пункты подменю «Службы» по данным services.json и статусам из кэша
        /// (issue #11, 1.0.0.14): построение не блокирует UI — статусы читаются
        /// из <see cref="ServiceStatusCache"/> и обновляются фоном.
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
            var cache = ServicesStatusCache();
            EnsureServicesStatusTimer();
            return builder.Build(data,
                name => cache?.GetStatus(name) ?? new ServiceInfo { ServiceName = name, Status = ServiceStatus.Unknown },
                ServicePlatform.Windows);
        }

        /// <summary>Единый кэш статусов из DI (issue #3, часть 2): трей, главное окно
        /// и меню «СЛУЖБЫ» монитора серверов читают статусы из одного экземпляра.</summary>
        private Services.ServiceStatusCache? ServicesStatusCache()
        {
            if (_serviceStatusCache is null)
                _serviceStatusCache = AppServices.TryGetService<Services.ServiceStatusCache>();
            return _serviceStatusCache;
        }

        /// <summary>Запускает фоновый таймер обновления кэша статусов (5 с).
        /// Опросы — в рабочих потоках, UI не блокируется.</summary>
        private void EnsureServicesStatusTimer()
        {
            if (_servicesStatusTimer is not null)
                return;
            _servicesStatusTimer = new System.Threading.Timer(
                _ => RefreshServicesStatusesInBackground(),
                null,
                TimeSpan.Zero, // первый опрос сразу — прогрев кэша (issue #11, 1.0.0.18)
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

        /// <summary>Подменю «Службы» для штатного меню трея (WinForms-адаптер модели
        /// ServiceTrayMenuBuilder). <paramref name="title"/> — заголовок подменю
        /// из компоновки (по умолчанию — ключ «Tray.Services»).</summary>
        private Forms.ToolStripMenuItem BuildServicesSubmenu(string? title = null)
        {
            var submenu = CreateTrayItem(title ?? LocalizationManager.T("Tray.Services"), TrayIconKind.Service, null);
            ApplyModernDropDownStyle(submenu.DropDown);
            foreach (var item in BuildServicesMenuItems())
                submenu.DropDownItems.Add(ToServiceTrayItem(item));
            return submenu;
        }

        /// <summary>
        /// Ctrl+ПКМ: показываем меню управления службами вместо обычного (7OH,
        /// «если получится» — Windows). Подавление штатного меню перенесено на
        /// MouseDown (<see cref="OnTrayIconMouseDown"/>) — до старта системного
        /// показа, чтобы WinForms не успел показать обычное меню поверх (гонка).
        /// Меню служб строится из прогретого кэша статусов (мгновенно, без
        /// синхронных опросов) и НЕ показывается вручную через Show(): оно
        /// подменяет штатный ContextMenuStrip трея, поэтому системный показ
        /// NotifyIcon открывает его как обычное меню — с корректным владельцем
        /// (внутреннее окно NotifyIcon), из-за чего клик вне меню и ESC закрывают
        /// его, как обычное меню (замечание 7OH от 19.10: ручной Show() из
        /// v1.0.0.18 не закрывался). Возврат обычного меню — по закрытии меню
        /// служб (Closed). Правый клик без Ctrl открывает обычное меню, как раньше.
        /// </summary>
        private Forms.ContextMenuStrip? BuildServicesTrayMenu()
        {
            var servicesMenu = CreateModernTrayMenu();
            foreach (var item in BuildServicesMenuItems())
                servicesMenu.Items.Add(ToServiceTrayItem(item));
            servicesMenu.Closed += (_, _) => RestoreTrayMenuAfterCtrl();
            return servicesMenu;
        }

        /// <summary>MouseDown: при Ctrl+ПКМ заранее подавляем штатное меню (до старта
        /// системного показа) и подменяем его менем служб — системный показ NotifyIcon
        /// откроет меню служб нативно (закрытие по клику вне меню и ESC работает, как
        /// у обычного меню).</summary>
        private void OnTrayIconMouseDown(object? sender, Forms.MouseEventArgs e)
        {
            if (e.Button != Forms.MouseButtons.Right)
                return;

            if (!TrayServicesMenuDecider.ShouldSwapOnMouseDown(
                    IsCtrlDown(),
                    AppServices.TryGetService<IServiceControllerService>() is not null,
                    _ctrlRightClickPending))
                return;

            if (_trayIcon is null)
                return;

            _ctrlRightClickPending = true;
            _suppressedTrayMenu = _trayIcon.ContextMenuStrip;
            _servicesTrayMenu = BuildServicesTrayMenu();
            // Подмена штатного меню меню служб: системный показ (WM_CONTEXTMENU)
            // откроет именно его. Если службы недоступны (строитель вернул null-меню
            // не бывает, но страховка) — просто не трогаем штатное меню.
            if (_servicesTrayMenu is not null)
                _trayIcon.ContextMenuStrip = _servicesTrayMenu;
            else
                RestoreTrayMenuAfterCtrl();
        }

        /// <summary>Правый клик по иконке трея: Ctrl+ПКМ — меню служб, иначе — штатное меню.</summary>
        private void OnTrayIconMouseUp(object? sender, Forms.MouseEventArgs e)
        {
            if (e.Button != Forms.MouseButtons.Right)
                return;

            // Ctrl зажат — меню служб уже подменено в MouseDown, системный показ сам
            // откроет его после MouseUp (как обычное меню — с нативным закрытием).
            // Действий здесь не требуется; Show() не вызываем: он не использует
            // владельца NotifyIcon и ломает закрытие по клику вне меню/ESC.
            if (TrayServicesMenuDecider.ShouldRestoreOnMouseUp(IsCtrlDown(), _ctrlRightClickPending))
                RestoreTrayMenuAfterCtrl();
        }

        /// <summary>Восстанавливает штатное меню трея после подавления/закрытия Ctrl+ПКМ
        /// (вызывается и из Closed меню служб, и при отпущенном Ctrl между down/up).</summary>
        private void RestoreTrayMenuAfterCtrl()
        {
            _ctrlRightClickPending = false;
            _servicesTrayMenu = null;
            if (_trayIcon is not null)
                _trayIcon.ContextMenuStrip = _suppressedTrayMenu ?? CreateMainTrayMenu();
            _suppressedTrayMenu = null;
        }

        // =====================================================================
        // Нативная проверка Ctrl (issue #11, 1.0.0.18): GetKeyState читает состояние
        // клавиши без фокуса окна — WPF Keyboard.Modifiers для трея ненадёжен
        // (окно может не иметь фокуса). WPF-проверка остаётся fallback'ом.
        // =====================================================================

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int nVirtKey);

        private const int VkControl = 0x11;

        /// <summary>Надёжная проверка Ctrl: нативный GetKeyState, WPF Keyboard.Modifiers — fallback.</summary>
        private static bool IsCtrlDown()
        {
            try
            {
                if ((GetKeyState(VkControl) & 0x8000) != 0)
                    return true;
            }
            catch
            {
                // Нативный вызов недоступен — fallback ниже
            }
            return (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        }

        /// <summary>«Настроить…» — окно «Управление службами» (требование 7OH: кнопка настройки сверху).</summary>
        private void ShowServicesSettings()
        {
            if (_viewModel.ShowServicesCommand.CanExecute(null))
                _viewModel.ShowServicesCommand.Execute(null);
        }

        /// <summary>Адаптер пункта модели меню служб к WinForms (ToolStripItem).</summary>
        private Forms.ToolStripItem ToServiceTrayItem(ServiceMenuItem item)
        {
            switch (item.Kind)
            {
                case ServiceMenuItemKind.Separator:
                    return new Forms.ToolStripSeparator();

                case ServiceMenuItemKind.Settings:
                    return CreateTrayItem(item.Text, TrayIconKind.Settings, (_, _) => ShowServicesSettings());

                case ServiceMenuItemKind.ServiceSubmenu:
                {
                    var node = CreateTrayItem(item.Text, TrayIconKind.Service, null);
                    // Статус службы отражается кружком (цвет StatusColorHex) — как кружок
                    // статуса строки на панели окна «Управление службами» (иконки в трее
                    // = иконки панели, замечание 7OH 18.10).
                    node.Image = DrawStatusDot(item.StatusColorHex) ?? node.Image;
                    node.ToolTipText = item.StatusText;
                    ApplyModernDropDownStyle(node.DropDown);
                    foreach (var child in item.Children)
                        node.DropDownItems.Add(ToServiceTrayItem(child));
                    return node;
                }

                case ServiceMenuItemKind.GroupSubmenu:
                {
                    var node = CreateTrayItem(item.Text, TrayIconKind.Group, null);
                    // Микс иконок (замечание 7OH 19.10): папка + кружок статуса группы
                    // справа-снизу (цвет StatusColorHex — зелёный/жёлтый/серый, как кружок
                    // статуса группы на панели окна «Управление службами»); статус группы —
                    // подсказкой (текст «Все запущены»/«Частично»/…).
                    node.Image = DrawGroupStatusIcon(item.StatusColorHex) ?? node.Image;
                    node.ToolTipText = item.StatusText;
                    ApplyModernDropDownStyle(node.DropDown);
                    foreach (var child in item.Children)
                        node.DropDownItems.Add(ToServiceTrayItem(child));
                    return node;
                }

                case ServiceMenuItemKind.AllServicesSubmenu:
                {
                    var node = CreateTrayItem(item.Text, TrayIconKind.Group, null);
                    ApplyModernDropDownStyle(node.DropDown);
                    foreach (var child in item.Children)
                        node.DropDownItems.Add(ToServiceTrayItem(child));
                    return node;
                }

                case ServiceMenuItemKind.ServiceCommand:
                case ServiceMenuItemKind.GroupCommand:
                case ServiceMenuItemKind.AllServicesCommand:
                    return BuildServiceCommandItem(item);

                default:
                    return new Forms.ToolStripMenuItem(item.Text) { Enabled = item.IsEnabled };
            }
        }

        /// <summary>
        /// Пункт-команда меню служб: иконка — та же, что у кнопки панели окна
        /// «Управление службами» (▶ зелёный, ■ красный, ❚❚ оранжевый, ▶| синий,
        /// ⟲ синий; замечание 7OH 18.10 «иконки в трее — такие же как на панели»),
        /// доступность — IsEnabled построителя.
        /// </summary>
        private Forms.ToolStripMenuItem BuildServiceCommandItem(ServiceMenuItem item)
        {
            var action = item.Action ?? ServiceAction.Start;
            var command = new Forms.ToolStripMenuItem(item.Text)
            {
                Enabled = item.IsEnabled,
                Image = GetCommandGlyph(action),
                ImageScaling = Forms.ToolStripItemImageScaling.None,
                DisplayStyle = Forms.ToolStripItemDisplayStyle.ImageAndText,
                ImageAlign = Drawing.ContentAlignment.MiddleLeft,
                TextAlign = Drawing.ContentAlignment.MiddleLeft,
                Padding = new Forms.Padding(2, 3, 4, 3),
                AutoSize = true
            };
            var target = item.Target;
            var targetName = item.TargetName;
            var names = item.ServiceNames.ToList();
            command.Click += (_, _) => ExecuteTrayServiceOperation(action, target, targetName, names);
            return command;
        }

        /// <summary>
        /// Выполняет операцию над целевыми службами (отдельная служба / члены
        /// группы / все службы списка) в фоновом потоке — не блокируя UI, после
        /// чего пересобирает меню трея (статусы изменились).
        /// </summary>
        private void ExecuteTrayServiceOperation(
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

            // Операции со службами блокирующие (ServiceController) — в фоне
            // (требование Задачи 4: не блокировать UI-поток).
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
                                action, info.Status, info.CanPauseAndContinue, info.CanStop, ServicePlatform.Windows))
                            continue;
                        ExecuteServiceAction(services, action, name);
                    }
                    catch
                    {
                        // Отдельная служба не должна останавливать остальные.
                    }
                }
            }).ContinueWith(_ => Dispatcher.BeginInvoke(new Action(RebuildTrayMenuFromServices)), TaskScheduler.Default);
        }

        /// <summary>Пересборка меню трея после операции со службами (статусы обновились).</summary>
        private void RebuildTrayMenuFromServices()
        {
            // Статусы изменились — сбрасываем кэш и опрашиваем заново в фоне.
            _serviceStatusCache?.Invalidate();
            RefreshServicesStatusesInBackground();
            if (_trayIcon?.ContextMenuStrip is { } menu)
                RebuildTrayMenu(menu);
        }

        private static bool ExecuteServiceAction(IServiceControllerService services, ServiceAction action, string serviceName) =>
            action switch
            {
                ServiceAction.Start => services.Start(serviceName),
                ServiceAction.Stop => services.Stop(serviceName),
                ServiceAction.Pause => services.Pause(serviceName),
                ServiceAction.Continue => services.Continue(serviceName),
                ServiceAction.Restart => services.Restart(serviceName),
                _ => false
            };

        /// <summary>Цвет глифа операции (единый источник — <see cref="ServiceCommandGlyphs"/>).</summary>
        private static string CommandColorHex(ServiceAction? action)
            => ServiceCommandGlyphs.ColorHex(action ?? ServiceAction.Start);

        /// <summary>Кэш кружков статуса/команд по цвету (#RRGGBB).</summary>
        private static readonly Dictionary<string, Drawing.Image> StatusDotCache = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Кэш глифов команд служб по операции.</summary>
        private static readonly Dictionary<ServiceAction, Drawing.Image> CommandGlyphCache = new();

        /// <summary>Кэш иконок «папка + кружок статуса» по цвету (#RRGGBB).</summary>
        private static readonly Dictionary<string, Drawing.Image> GroupStatusIconCache = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Глиф команды службы 16×16 — тот же, что у кнопки панели окна «Управление
        /// службами» (замечание 7OH 18.10: «иконки в трее — такие же как на панели»):
        /// ▶ (Запустить), ■ (Остановить), ❚❚ (Пауза), ▶| (Восстановить — треугольник
        /// с палочкой справа), ⟲ (Перезапустить). Рисуется через GDI+ по геометрии
        /// глифов — без зависимости от шрифтов и WPF visual tree.
        /// </summary>
        private static Drawing.Image GetCommandGlyph(ServiceAction action)
        {
            if (CommandGlyphCache.TryGetValue(action, out var cached))
                return cached;
            var img = DrawCommandGlyph(action, TrayMenuIconSize);
            CommandGlyphCache[action] = img;
            return img;
        }

        private static Drawing.Image DrawCommandGlyph(ServiceAction action, int size)
        {
            var bmp = new Drawing.Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var g = Drawing.Graphics.FromImage(bmp);
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.PixelOffsetMode = Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.Clear(Drawing.Color.Transparent);

            var color = Drawing.ColorTranslator.FromHtml(CommandColorHex(action));
            using var brush = new Drawing.SolidBrush(color);
            using var pen = new Drawing.Pen(color, Math.Max(1.4f, size / 11f))
            {
                StartCap = Drawing.Drawing2D.LineCap.Round,
                EndCap = Drawing.Drawing2D.LineCap.Round,
                LineJoin = Drawing.Drawing2D.LineJoin.Round
            };

            float s = size;
            switch (action)
            {
                case ServiceAction.Start:
                    // ▶ — зелёный треугольник (как кнопка «Запустить»).
                    g.FillPolygon(brush, new[]
                    {
                        new Drawing.PointF(s * 0.26f, s * 0.18f),
                        new Drawing.PointF(s * 0.82f, s * 0.50f),
                        new Drawing.PointF(s * 0.26f, s * 0.82f)
                    });
                    break;

                case ServiceAction.Stop:
                    // ■ — красный квадрат (как кнопка «Остановить»).
                    g.FillRectangle(brush, s * 0.22f, s * 0.22f, s * 0.56f, s * 0.56f);
                    break;

                case ServiceAction.Pause:
                    // ❚❚ — оранжевые две палочки (как кнопка «Пауза»).
                    g.FillRectangle(brush, s * 0.22f, s * 0.18f, s * 0.18f, s * 0.64f);
                    g.FillRectangle(brush, s * 0.60f, s * 0.18f, s * 0.18f, s * 0.64f);
                    break;

                case ServiceAction.Continue:
                    // ▶| — синий треугольник «Запустить» + вертикальная палочка справа
                    // (замечание 7OH 18.10: «палочку дорисовать к треугольнику»).
                    g.FillPolygon(brush, new[]
                    {
                        new Drawing.PointF(s * 0.18f, s * 0.18f),
                        new Drawing.PointF(s * 0.66f, s * 0.50f),
                        new Drawing.PointF(s * 0.18f, s * 0.82f)
                    });
                    g.FillRectangle(brush, s * 0.74f, s * 0.18f, s * 0.16f, s * 0.64f);
                    break;

                case ServiceAction.Restart:
                default:
                    // ⟲ — синяя круговая стрелка (как кнопка «Перезапустить»).
                    g.DrawArc(pen, s * 0.18f, s * 0.18f, s * 0.64f, s * 0.64f, -40, 200);
                    g.FillPolygon(brush, new[]
                    {
                        new Drawing.PointF(s * 0.72f, s * 0.12f),
                        new Drawing.PointF(s * 0.92f, s * 0.32f),
                        new Drawing.PointF(s * 0.62f, s * 0.34f)
                    });
                    break;
            }

            return bmp;
        }

        /// <summary>Рисует кружок TrayMenuIconSize×TrayMenuIconSize цвета #RRGGBB
        /// (статус службы/группы, цвет команды).</summary>
        private static Drawing.Image? DrawStatusDot(string? colorHex)
        {
            if (string.IsNullOrWhiteSpace(colorHex))
                return null;
            if (StatusDotCache.TryGetValue(colorHex, out var cached))
                return cached;

            Drawing.Color color;
            try { color = Drawing.ColorTranslator.FromHtml(colorHex); }
            catch { color = Drawing.Color.FromArgb(107, 114, 128); }

            var bmp = new Drawing.Bitmap(TrayMenuIconSize, TrayMenuIconSize, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var g = Drawing.Graphics.FromImage(bmp);
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Drawing.Color.Transparent);
            using var brush = new Drawing.SolidBrush(color);
            // Кружок с отступом 0.1875 и диаметром 0.625 от размера (для 20: 3.75 и 12.5).
            var o = TrayMenuIconSize * 0.1875f;
            var d = TrayMenuIconSize * 0.625f;
            g.FillEllipse(brush, o, o, d, d);
            StatusDotCache[colorHex] = bmp;
            return bmp;
        }

        /// <summary>
        /// Иконка группы служб TrayMenuIconSize×TrayMenuIconSize — «микс» (замечание
        /// 7OH 19.10): папка (та же геометрия, что у <see cref="TrayIconKind.Group"/>) +
        /// маленький кружок статуса группы справа-снизу (цвет <paramref name="statusColorHex"/>
        /// — зелёный/жёлтый/серый, как кружок статуса группы на панели окна «Управление
        /// службами»). Рисуется через GDI+ по геометрии папки — без зависимости от шрифтов
        /// и WPF visual tree. null — без статуса (остаётся обычная папка).
        /// </summary>
        private static Drawing.Image? DrawGroupStatusIcon(string? statusColorHex)
        {
            if (string.IsNullOrWhiteSpace(statusColorHex))
                return null;
            if (GroupStatusIconCache.TryGetValue(statusColorHex, out var cached))
                return cached;

            var bmp = new Drawing.Bitmap(TrayMenuIconSize, TrayMenuIconSize, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var g = Drawing.Graphics.FromImage(bmp);
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.PixelOffsetMode = Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.Clear(Drawing.Color.Transparent);

            // Папка — та же геометрия, что у TrayIconKind.Group (фиолетовая).
            var folderColor = Drawing.Color.FromArgb(139, 92, 246);
            using var folderBrush = new Drawing.SolidBrush(folderColor);
            using var folderPen = new Drawing.Pen(folderColor, Math.Max(1.4f, TrayMenuIconSize / 11f))
            {
                StartCap = Drawing.Drawing2D.LineCap.Round,
                EndCap = Drawing.Drawing2D.LineCap.Round,
                LineJoin = Drawing.Drawing2D.LineJoin.Round
            };
            float s = TrayMenuIconSize;
            g.FillRectangle(folderBrush, s * 0.10f, s * 0.32f, s * 0.80f, s * 0.50f);
            g.DrawLine(folderPen, s * 0.10f, s * 0.36f, s * 0.42f, s * 0.36f);
            g.FillRectangle(folderBrush, s * 0.10f, s * 0.28f, s * 0.30f, s * 0.10f);

            // Кружок статуса справа-снизу (с белой обводкой для контраста на папке).
            Drawing.Color color;
            try { color = Drawing.ColorTranslator.FromHtml(statusColorHex); }
            catch { color = Drawing.Color.FromArgb(107, 114, 128); }
            using var dotBrush = new Drawing.SolidBrush(color);
            using var ring = new Drawing.Pen(Drawing.Color.White, 1.2f * s / 16f);
            var dot = s * 0.34375f;   // 5.5/16 — диаметр кружка
            var dotX = s * 0.59375f;  // 9.5/16 — левый верхний угол кружка
            g.FillEllipse(dotBrush, dotX, dotX, dot, dot);
            g.DrawEllipse(ring, dotX, dotX, dot, dot);

            GroupStatusIconCache[statusColorHex] = bmp;
            return bmp;
        }

        /// <summary>Стиль подменю служб: современный рендерер, шрифт и отступы (как у основного меню).</summary>
        private static void ApplyModernDropDownStyle(Forms.ToolStripDropDown dropDown)
        {
            dropDown.Renderer = new ModernTrayMenuRenderer();
            dropDown.Font = new Drawing.Font("Segoe UI", 9.5f, Drawing.FontStyle.Regular);
            dropDown.ImageScalingSize = new Drawing.Size(TrayMenuIconSize, TrayMenuIconSize);
            dropDown.Padding = new Forms.Padding(2, 2, 2, 2);
            dropDown.Margin = new Forms.Padding(0);
            if (dropDown is Forms.ToolStripDropDownMenu menu)
            {
                menu.ShowImageMargin = true;
                menu.ShowCheckMargin = false;
            }
        }

    }
}
#endif
