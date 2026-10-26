Исправлено в версии 1.0.0.77 (Windows и Linux) по описанию issue:

1. **Исправлен NRE при открытии диалога «Дублировать серверную базу».** Событие `Checked` радиокнопки режима срабатывало до создания `HintText` — добавлен guard в `UpdateModeHint` (WPF + Avalonia) ([`CloneServerInfobaseWindow.xaml.cs`](Workspace1C/Views/CloneServerInfobaseWindow.xaml.cs), [`CloneServerInfobaseWindow.Avalonia.cs`](Workspace1C/Views/CloneServerInfobaseWindow.Avalonia.cs)).

Покрыто тестами `Issue70CloneServerWindowInitTests`.

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.77
