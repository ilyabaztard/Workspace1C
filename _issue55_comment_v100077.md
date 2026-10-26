Исправлено в версии 1.0.0.77 (Windows и Linux) по последнему комментарию (обрезание элементов в окнах «Сменить платформу» и «Выбор родительской группы», аудит всех форм):

1. **Окно «Сменить платформу»** — исправлено обрезание элементов при уменьшении окна: Min-размеры, Grid Auto+*, ScrollViewer, перенос текста ([`PlatformChangeWindow.xaml`](Workspace1C/Views/PlatformChangeWindow.xaml)).
2. **Окно «Выбор родительской группы»** — аналогично ([`GroupPickerWindow.xaml`](Workspace1C/Views/GroupPickerWindow.xaml)).
3. **Аудит всех форм** — Min-размеры добавлены для редакторов меню, ConfigDiffResultWindow, CloneServerInfobaseWindow, CreateInfobaseWindow и др. ([`ConnectionSettingsWindow.xaml`](Workspace1C/Views/ConnectionSettingsWindow.xaml)).

Покрыто тестами `Issue55WindowLayoutTests`.

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.77
