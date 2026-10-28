Исправлено в версии 1.0.0.95 (Windows и Linux):

1. **Переключение на другой сервер 1С в окне «Серверы 1С» всегда заканчивалось ошибкой** (кнопка «Подключиться» при выбранной строке справочника или двойной клик): данные загружались не сразу, а только после нажатия «Обновить» или повторного двойного клика. Причина: при повторном подключении не сбрасывался выбор кластера — `SelectedClusterId` оставался от ПРЕДЫДУЩЕГО сервера. Применение нового списка кластеров в WPF/Avalonia идёт в UI-поток отложенно (Dispatcher.BeginInvoke / UIThread.Post), а `ConnectAsync` после `ConfigureAwait(false)` продолжается в фоне — поэтому первичная загрузка стартовала со СТАРЫМ идентификатором кластера на новом сервере, и rac возвращал ошибку.

2. Теперь при старте подключения состояние кластера сбрасывается синхронно (выбор кластера, список кластеров, данные вкладок, фильтр по базе), а автовыбор первого кластера берётся из только что полученного списка «cluster list», а не из устаревших `SelectedClusterId`/`ClusterRows` ([`ServerMonitorViewModel.cs`](Workspace1C/ViewModels/ServerMonitorViewModel.cs)). Переключение на другой сервер загружает кластеры/базы/сеансы/соединения сразу, без промежуточного «Обновить». Исправление общее для WPF ([`ServerMonitorWindow.xaml.cs`](Workspace1C/Views/ServerMonitorWindow.xaml.cs)) и Avalonia ([`ServerMonitorWindow.Avalonia.cs`](Workspace1C/Views/ServerMonitorWindow.Avalonia.cs)).

Покрыто регрессионными тестами `ReconnectToAnotherServer_LoadsClusterOfNewServer_FirstTry_Issue77` и `SwitchServer_WithDeferredApply_LoadsClusterOfNewServer_FirstTry_Issue77` ([`ServerMonitorViewModelTests.cs`](Workspace1C.Tests/ServerMonitorViewModelTests.cs)).

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.95
