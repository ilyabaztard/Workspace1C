# Workspace 1C 1.0.0.77

Релиз доступен для **Windows** (WPF) и **Linux** (Avalonia).

### Добавлено

- **Массовые действия над выбранными базами: тонкие клиенты в списке платформ
  (issue #54, Windows и Linux)** — в окне «Сменить платформу» тонкие клиенты
  выделяются суффиксом «(тонкий клиент)» и оранжевой подсветкой, как в
  «Настройках» ([`PlatformDisplayFormatter.cs`](Workspace1C/Services/PlatformDisplayFormatter.cs),
  [`PlatformChangeViewModel.cs`](Workspace1C/ViewModels/PlatformChangeViewModel.cs),
  [`PlatformChangeWindow.xaml`](Workspace1C/Views/PlatformChangeWindow.xaml)). Покрыто
  тестами `PlatformChangeViewModelTests`, `Issue54ThinClientHighlightTests`.

- **Публикация баз: возвращён выбор include-файла алиасов и значения по умолчанию
  из конфига службы (issue #63, Windows и Linux)** — в окне «Публикация на
  веб-сервере…» снова доступен выбор include-файла алиасов (ConfTargets); значения
  по умолчанию берутся из конфига службы (`-f`), а не из дефолтного httpd.conf;
  поле выбора сервера расширено до 400px; в подписи инстанса показывается имя
  конфига ([`ApacheInstanceDetector.cs`](Workspace1C/Services/ApacheInstanceDetector.cs),
  [`WebPublishViewModel.cs`](Workspace1C/ViewModels/WebPublishViewModel.cs),
  [`WebPublishWindow.cs`](Workspace1C/Views/WebPublishWindow.cs)). Покрыто тестами
  `Apache27ConfRegressionTests`, `ApacheInstanceDetectorTests`,
  `WebPublishIssue63Tests`, `WebPublishServerComboBoxWidthTests`.

### Исправлено

- **«Дублировать базу»: исправлен NRE при открытии диалога (issue #70, Windows и
  Linux)** — событие Checked радиокнопки режима срабатывало до создания HintText;
  добавлен guard в UpdateModeHint (WPF + Avalonia)
  ([`CloneServerInfobaseWindow.xaml.cs`](Workspace1C/Views/CloneServerInfobaseWindow.xaml.cs),
  [`CloneServerInfobaseWindow.Avalonia.cs`](Workspace1C/Views/CloneServerInfobaseWindow.Avalonia.cs)).
  Покрыто тестами `Issue70CloneServerWindowInitTests`.

- **Удаление из кластера: упрощена подсказка, окно увеличено, пароль сохраняется,
  исправлена ошибка rac 8.5.4 (issue #69, Windows и Linux)** — текст подсказки
  диалога повтора упрощён (убрана фраза про завершение сеансов «во вкладке»),
  окно увеличено (кнопки не обрезаются), пароль сохраняется между попытками;
  исправлена ошибка rac 8.5.4 «Ошибка разбора параметра: process» — session
  terminate / connection disconnect теперь передают `--process=<pid>`
  ([`IRacClient.cs`](Workspace1C/Services/IRacClient.cs),
  [`RacClient.cs`](Workspace1C/Services/RacClient.cs),
  [`ServerMonitorViewModel.cs`](Workspace1C/ViewModels/ServerMonitorViewModel.cs),
  [`RacInfobaseDropRetryWindow.xaml`](Workspace1C/Views/RacInfobaseDropRetryWindow.xaml)).
  Покрыто тестами `Issue69DropRetryWindowLayoutTests`, `ServerMonitorViewModelTests`,
  `RacClientTests`.

- **Тёмная версия: подсветка строк ListView больше не «тёмная на тёмном» (issue #61,
  Windows и Linux)** — добавлен темизированный стиль `ModernListViewItem` и неявные
  стили `ListView` и `GridViewColumnHeader` в обе темы WPF: подсветка строк в
  «Конфигураторе параметров запуска» и других окнах с ListView+GridView больше не
  «тёмная на тёмном» и не «белая на белом»
  ([`DarkTheme.xaml`](Workspace1C/Themes/DarkTheme.xaml),
  [`LightTheme.xaml`](Workspace1C/Themes/LightTheme.xaml),
  [`CustomActionsWindow.xaml`](Workspace1C/Views/CustomActionsWindow.xaml)). Покрыто
  тестами `Issue61DarkPaletteAuditTests`, `Issue61TitleColorsTests`.

- **Формы и элементы: устранено обрезание при уменьшении окон (issue #55, Windows
  и Linux)** — исправлено обрезание элементов при уменьшении окон «Сменить
  платформу» и «Выбор родительской группы» (Min-размеры, Grid Auto+*, ScrollViewer,
  перенос текста); проведён аудит всех форм: Min-размеры для редакторов меню,
  ConfigDiffResultWindow, CloneServerInfobaseWindow, CreateInfobaseWindow и др.
  ([`PlatformChangeWindow.xaml`](Workspace1C/Views/PlatformChangeWindow.xaml),
  [`GroupPickerWindow.xaml`](Workspace1C/Views/GroupPickerWindow.xaml),
  [`ConnectionSettingsWindow.xaml`](Workspace1C/Views/ConnectionSettingsWindow.xaml)).
  Покрыто тестами `Issue55WindowLayoutTests`.

### Подтверждено

- **Общая авторизация (issue #57)** — подтверждено пользователем («Есть - теперь
  раздельно»). Правок кода в этом релизе не потребовалось.

Подробности — в [CHANGELOG.md](https://github.com/sivatorov/Workspace1C/blob/main/CHANGELOG.md).
