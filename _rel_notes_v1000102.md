# Workspace 1C 1.0.0.102

Релиз доступен для **Windows** (WPF) и **Linux** (Avalonia).
Скачать: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.102

### Исправлено

- **Контраст и цвета тёмной темы (issue #26, последние комментарии 7OH от 2026-10-27/28)**:

  1. **Тёмные надписи «1С Стартер»/«По ссылке» на главной.** Подписи вторичных кнопок
     панели действий брали `ButtonTextBrush`, который чёрный в обеих темах и на тёмной
     подложке карточки не читался. Переведены на основной цвет темы —
     `TextPrimaryBrush` (WPF) / `TextPrimaryColorBrush` (Avalonia); контраст
     `#F1F5F9` на `#1E293B` по WCAG ≈ 11.5:1
     ([`MainWindow.xaml`](Workspace1C/Views/MainWindow.xaml),
     [`MainWindow.Avalonia.Tree.cs`](Workspace1C/Views/MainWindow.Avalonia.Tree.cs)).
  2. **Белые переключатели на отдельной странице настроек.** Пилюля видимости колонок
     и шаблоны `SettingsToggle` использовали жёсткую светлую дорожку `#CBD5E1` — в
     тёмной теме выключенный тумблер выглядел белым. Введена единая кисть темы
     `SettingsToggleTrackBrush` (#475569 в тёмной, #CBD5E1 в светлой) на обеих
     платформах ([`SettingsWindow.xaml`](Workspace1C/Views/SettingsWindow.xaml),
     [`LightTheme.xaml`](Workspace1C/Themes/LightTheme.xaml),
     [`DarkTheme.xaml`](Workspace1C/Themes/DarkTheme.xaml),
     [`LightTheme.axaml`](Workspace1C/Themes/LightTheme.axaml),
     [`DarkTheme.axaml`](Workspace1C/Themes/DarkTheme.axaml)).
  3. **«Новый артефакт» и «формы по одному шаблону» (04:41).** Маленькие диалоги всё
     ещё рисовали жёсткую зелёную кнопку `#16A34A` с белым текстом в обход единого
     контракта кнопок 1.0.0.82. Переведены на `ModernButton`/`SecondaryButton`
     ([`MaterialMessageWindow.xaml`](Workspace1C/Services/MaterialMessageWindow.xaml),
     [`UpdateAvailableWindow.xaml`](Workspace1C/Services/UpdateAvailableWindow.xaml),
     [`MaterialMessageWindow.Avalonia.cs`](Workspace1C/Services/MaterialMessageWindow.Avalonia.cs),
     [`ManualUpdateWindow.Avalonia.cs`](Workspace1C/Services/ManualUpdateWindow.Avalonia.cs)).
  4. **Отличие маленького окна на Linux от Windows (04:45).** Кнопка «ОК» всплывающего
     окна на Linux красилась жёсткой зелёной заливкой (`#16A34A`, `Brushes.White`),
     кнопка отмены — чёрным текстом; после унификации (п. 3) окно на обеих платформах
     рисуется одинаково: акцентная кнопка подтверждения + вторичная отмена.
  5. **«Зебра» в таблицах (05:03).** Чередование строк переведено на тематическую
     кисть `ZebraRowBrush` (#334155 в тёмной, #F1F5F9 в светлой): исправлены окна с
     `AlternationCount` без тематической строки (`ConfigTypeEditWindow`,
     `PortsEditWindow`), стили `DataGrid.RowStyle` с `AlternationIndex` и построчная
     «зебра» Avalonia. Светлых/белых полос в тёмной теме больше нет.

### Тесты

- [`Issue26ContrastToggleZebraGuardTests.cs`](Workspace1C.Tests/Issue26ContrastToggleZebraGuardTests.cs):
  +13 — контраст подписей главной (WCAG ≥ 4.5), переключатели не белые
  (`SettingsToggleTrackBrush` в обеих темах и палитре), диалоги без жёстких зелёных
  кнопок, «зебра» таблиц тематическая (`ZebraRowBrush` во всех таблицах с
  чередованием). Всего в проекте **4 908 юнит-тестов**.

Подробности — в [CHANGELOG.md](https://github.com/sivatorov/Workspace1C/blob/main/CHANGELOG.md).
