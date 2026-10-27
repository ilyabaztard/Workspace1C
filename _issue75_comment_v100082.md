Исправлено в версии 1.0.0.82 (Windows и Linux):

1. **Колонки по содержимому заголовка** — на вкладках «Сеансы», «Соединения», «Блокировки» и «Рабочие процессы» монитора серверов колонки получают по умолчанию ширину, достаточную для отображения самого заголовка и пиктограммы направления сортировки (MinWidth под самый длинный заголовок в ru/en) ([`ServerMonitorWindow.xaml`](Workspace1C/Views/ServerMonitorWindow.xaml), [`ServerMonitorWindow.Avalonia.cs`](Workspace1C/Views/ServerMonitorWindow.Avalonia.cs)).
2. **Горизонтальная прокрутка** — таблицы получили горизонтальную прокрутку: колонки могут уходить за границу окна, а при сужении окна появляется именно полоса прокрутки, а не «схлопывание» колонок (WPF и Avalonia).
3. **Сохранение размеров колонок по вкладкам** — размеры колонок каждой вкладки сохраняются отдельно (новое хранилище `ServerMonitorColumnWidthsStore`) и переживают закрытие программы; при переключении вкладок восстановленные ширины применяются сразу ([`ServerMonitorColumnWidthsStore.cs`](Workspace1C/Services/ServerMonitorColumnWidthsStore.cs), [`AppSettings.cs`](Workspace1C/Models/AppSettings.cs)).

Покрыто тестами `ServerMonitorColumnWidthsTests` (+14), `Issue75ServerMonitorColumnsTests` (+6).

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.82