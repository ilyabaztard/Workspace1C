# Workspace 1C 1.0.1.0

Релиз доступен для **Windows** (WPF) и **Linux** (Avalonia).
Скачать: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.1.0

### Добавлено

- **Глобальная горячая клавиша показа главного окна (issue #82, Windows и Linux).**
  Настройка «Показать главное окно» на вкладке «Клавиши» окна «Настройки», по
  умолчанию **Ctrl+Alt+W**; применяется без перезапуска. Windows — нативный
  `RegisterHotKey`/`WM_HOTKEY`; Linux — X11 `XGrabKey` с фоновым циклом событий (на
  сессиях без X11 честно отключается). По нажатию окно восстанавливается и
  активируется — в том числе из трея.

### Исправлено

- **Контраст тёмной темы (issue #26, комментарии 7OH от 2026-10-28; Windows и
  Linux).** Подписи окна «Синхронизация» в Avalonia переведены с чёрного на кисти
  темы (`FormLabelBrush`/`TextSecondaryColorBrush`); подложка наведения кнопок окна
  в тёмной теме больше не «дневная» — полупрозрачный белый `#33FFFFFF` через
  `ThemeFeatureMap.ButtonMetrics(style, dark)`; стрелки сортировки заголовков
  DataGrid теперь тематический глиф **▲/▼** (`TextSecondaryBrush`) с триггерами
  `SortDirection`. Чёрные надписи переведены на контрастные кисти.

- **Иконка «Новинки программы» в реальном меню «Утилиты» (issue #81, Windows и
  Linux).** Легаси-layout'ы (`utilities_menu_layout.json` до 1.0.0.103), сохранившие
  запасную «папку» в группу, распознаются (`IsLegacyFallbackGroupIcon`) и
  сбрасываются к стандартной иконке NewBox (`FixLegacyNewItemsIcon`); хард-гарды в
  построителях WPF и Avalonia. Пользовательская иконка сохраняется.

- **Главное окно больше не открывается «сжатым» (issue #71, комментарии
  RizvanShikhammatov и 7OH).** Восстановленные из профиля размеры зажимаются к
  `MinWidth`/`MinHeight` в WPF и Avalonia. Стартовые размеры окон «Настройки»
  (1100×760) и «Настройка информационной базы» (900×800) при минимумах
  960×720/760×780 сохранены.

- **Мастер «Развернуть тестовую копию» (issue #79, комментарий 7OH от 2026-10-28
  07:51).** Единое тематическое оформление полей (`ModernTextBox`/`ModernComboBox`,
  WPF), автоподстановка «Имя базы данных СУБД» из «Имя базы на сервере» (пока поле
  пустое), кнопки «Свойства исходной базы…» (открывает настройки ИБ без выхода из
  мастера) и «Конфигуратор»; локализация ru/en.

### Тесты

- [`Issue26ContrastToggleZebraGuardTests`](Workspace1C.Tests/Issue26ContrastToggleZebraGuardTests.cs) (+6);
- [`Issue81NewBoxIconTests`](Workspace1C.Tests/Issue81NewBoxIconTests.cs) (+2);
- [`Issue71SettingsWindowLayoutTests`](Workspace1C.Tests/Issue71SettingsWindowLayoutTests.cs) (+2);
- [`Issue79TestDeployWindowTests`](Workspace1C.Tests/Issue79TestDeployWindowTests.cs) (новый);
- [`GlobalHotkeyTests`](Workspace1C.Tests/GlobalHotkeyTests.cs) (новый, + страж Avalonia-поля).

Итого в проекте **~4 958 юнит-тестов**; Linux-сборка (Avalonia) также зелёная.

Подробности — в [CHANGELOG.md](https://github.com/sivatorov/Workspace1C/blob/main/CHANGELOG.md).