# Workspace 1C 1.0.0.79

Релиз доступен для **Windows** (WPF) и **Linux** (Avalonia).
Скачать: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.79

### Добавлено

- **Контрастность текста на акцентном фоне и тёмных панелях (issue #26, Windows и
  Linux)** — кисть `TextOnAccentBrush` больше не берётся фиксированной из палитры, а
  пересчитывается контрастной к акценту (WCAG AA); для тёмных панелей (статус-бар,
  строка состояния, меню выбора окна) введена отдельная кисть `SidebarTextBrush`,
  контрастная к `SidebarColor`. Акцентные кнопки и тёмные панели переведены на
  контрастные кисти в обоих вариантах интерфейса
  ([`ThemeManager.cs`](Workspace1C/Themes/ThemeManager.cs),
  [`ThemeManager.Avalonia.cs`](Workspace1C/Themes/ThemeManager.Avalonia.cs),
  [`SettingsWindow.Schemes.cs`](Workspace1C/Views/SettingsWindow.Schemes.cs),
  [`SettingsWindow.Avalonia.Schemes.cs`](Workspace1C/Views/SettingsWindow.Avalonia.Schemes.cs)).
  Покрыто тестами `ThemeContrastRegressionTests`.

### Исправлено

- **Адаптивная раскладка форм: контент вкладок больше не обрезается при узком окне
  (issue #55, Windows и Linux)** — горизонтальная полоса прокрутки включена как
  fallback (Auto вместо Disabled), жёсткие ширины полей и комбобоксов заменены
  MinWidth, длинные подписи переносятся (TextWrapping), длинное имя базы обрезается
  многоточием; правки синхронизированы между WPF (*.xaml) и Avalonia
  (*.Avalonia.cs)
  ([`SettingsWindow.xaml`](Workspace1C/Views/SettingsWindow.xaml),
  [`SettingsWindow.Avalonia.cs`](Workspace1C/Views/SettingsWindow.Avalonia.cs),
  [`ConnectionSettingsWindow.xaml`](Workspace1C/Views/ConnectionSettingsWindow.xaml),
  [`ConnectionSettingsWindow.Avalonia.cs`](Workspace1C/Views/ConnectionSettingsWindow.Avalonia.cs),
  [`ServerEditWindow.xaml`](Workspace1C/Views/ServerEditWindow.xaml),
  [`ServerEditWindow.Avalonia.cs`](Workspace1C/Views/ServerEditWindow.Avalonia.cs),
  [`LaunchParameterEditWindow.xaml`](Workspace1C/Views/LaunchParameterEditWindow.xaml),
  [`LaunchParameterEditWindow.Avalonia.cs`](Workspace1C/Views/LaunchParameterEditWindow.Avalonia.cs),
  [`RacInfobaseCreateWindow.xaml`](Workspace1C/Views/RacInfobaseCreateWindow.xaml),
  [`RacInfobasePropertiesWindow.xaml`](Workspace1C/Views/RacInfobasePropertiesWindow.xaml),
  [`RacInfobasePropertiesWindow.Avalonia.cs`](Workspace1C/Views/RacInfobasePropertiesWindow.Avalonia.cs),
  [`ScriptScenarioEditWindow.xaml`](Workspace1C/Views/ScriptScenarioEditWindow.xaml),
  [`ScriptScenarioEditWindow.Avalonia.cs`](Workspace1C/Views/ScriptScenarioEditWindow.Avalonia.cs),
  [`ScriptPickWindow.xaml`](Workspace1C/Views/ScriptPickWindow.xaml)).
  Покрыто тестами `Issue55AdaptiveLayoutTests`.

- **Миграция баз: счётчик прогресса привязан к текущему прогону (issue #41, Windows и
  Linux)** — `ProgressIndex` — порядковый номер обрабатываемой базы в текущем прогоне
  (1-based: «1 из K», «2 из K»…), а не позиция в полном списке; `ProgressTotal` —
  количество отмеченных баз (стабильно в рамках прогона); счётчик `ProcessedCount`
  совпадает с `ProgressIndex`
  ([`ClusterMigrationViewModel.cs`](Workspace1C/ViewModels/ClusterMigrationViewModel.cs),
  [`ClusterMigrationRow.cs`](Workspace1C/ViewModels/ClusterMigrationRow.cs)). Покрыто
  тестами `ClusterMigrationViewModelTests`.

### Подтверждено

- **Общая авторизация (issue #57)** — подтверждено пользователем («Есть - теперь
  раздельно»). Правок кода в этом релизе не потребовалось.

Полный прогон тестов зелёный (4534 тестов).

Подробности — в [CHANGELOG.md](https://github.com/sivatorov/Workspace1C/blob/main/CHANGELOG.md).
