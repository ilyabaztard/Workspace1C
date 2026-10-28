Реализовано в версии **1.0.1.0** (Windows и Linux) — глобальная горячая клавиша
показа главного окна:

- **Что делает.** Глобальная клавиша «Показать главное окно» работает независимо от
  активного приложения: по нажатию главное окно восстанавливается и активируется —
  из системного трея, свёрнутого состояния или из-за окна другой программы.

- **Настройка.** В окне «Настройки», вкладка «Клавиши» (WPF и Avalonia) — новое поле
  через свойство [`AppSettings.GlobalShowWindowHotkey`](Workspace1C/Models/AppSettings.cs).
  По умолчанию **Ctrl+Alt+W** (как у Start Manager); пустое значение — клавиша не
  назначена. Изменение применяется сразу, без перезапуска приложения.

- **Платформенная реализация:**
  - Windows — нативный `RegisterHotKey`/`WM_HOTKEY`
    ([`GlobalHotkeyService.Windows.cs`](Workspace1C/Services/GlobalHotkeyService.Windows.cs));
  - Linux — X11 `XGrabKey` с фоновым циклом обработки событий
    ([`GlobalHotkeyService.Avalonia.cs`](Workspace1C/Services/GlobalHotkeyService.Avalonia.cs);
    на сессиях без X11 глобальная клавиша честно отключается);
  - общий контракт [`IGlobalHotkeyService`](Workspace1C/Services/IGlobalHotkeyService.cs)
    и валидация жеста [`GlobalHotkeyLogic`](Workspace1C/Services/GlobalHotkeyLogic.cs)
    (невалидные комбинации и занятые клавиши просто отключают регистрацию —
    приложение продолжает работать).

- Восстановление окна по клавише: [`MainWindow.Hotkeys.cs`](Workspace1C/Views/MainWindow.Hotkeys.cs)
  (WPF), [`MainWindow.Avalonia.cs`](Workspace1C/Views/MainWindow.Avalonia.cs) (Linux).

Покрыто тестами [`GlobalHotkeyTests.cs`](Workspace1C.Tests/GlobalHotkeyTests.cs)
(новый: контракт сервиса, валидация жеста, страж поля `GlobalShowWindowHotkey` в
настройках WPF и Avalonia).

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.1.0