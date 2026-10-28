Исправлено в версии **1.0.0.102** (Windows и Linux) по последним комментариям 7OH от 2026-10-27/28 (все 5 пунктов):

1. **«На главной "1С Стартер" и "По ссылке" — надписи тёмные» (2026-10-27 20:24).**
   Подписи вторичных кнопок панели действий брали `ButtonTextBrush`, который чёрный
   в обеих темах и на тёмной подложке карточки не читался. Переведены на основной
   цвет темы — `TextPrimaryBrush`/`TextPrimaryColorBrush` (WPF и Avalonia); контраст
   `#F1F5F9` на `#1E293B` по WCAG ≈ 11.5:1 (≥ 4.5)
   ([`MainWindow.xaml`](Workspace1C/Views/MainWindow.xaml),
   [`MainWindow.Avalonia.Tree.cs`](Workspace1C/Views/MainWindow.Avalonia.Tree.cs)).

2. **«Переключатели — на одной странице нормальные, а вот тут белые» (2026-10-27 20:25).**
   Пилюля видимости колонок окна настроек и шаблоны `SettingsToggle` использовали
   жёсткую светлую дорожку `#CBD5E1` — в тёмной теме выключенный тумблер выглядел
   белым. Введена единая кисть темы `SettingsToggleTrackBrush`: **#475569** в тёмной,
   #CBD5E1 в светлой — на обеих платформах (WPF и Avalonia)
   ([`SettingsWindow.xaml`](Workspace1C/Views/SettingsWindow.xaml),
   [`LightTheme.xaml`](Workspace1C/Themes/LightTheme.xaml),
   [`DarkTheme.xaml`](Workspace1C/Themes/DarkTheme.xaml),
   [`LightTheme.axaml`](Workspace1C/Themes/LightTheme.axaml),
   [`DarkTheme.axaml`](Workspace1C/Themes/DarkTheme.axaml)).

3. **«А вот и нового… Можно один раз правило сделать и делать формы по одному шаблону?» (04:41).**
   Маленькие диалоги всё ещё рисовали жёсткую зелёную кнопку `#16A34A` с белым
   текстом в обход единого контракта кнопок 1.0.0.82 (`ModernButton`/`SecondaryButton`).
   Переведены: [`MaterialMessageWindow.xaml`](Workspace1C/Services/MaterialMessageWindow.xaml),
   [`UpdateAvailableWindow.xaml`](Workspace1C/Services/UpdateAvailableWindow.xaml),
   [`MaterialMessageWindow.Avalonia.cs`](Workspace1C/Services/MaterialMessageWindow.Avalonia.cs),
   [`ManualUpdateWindow.Avalonia.cs`](Workspace1C/Services/ManualUpdateWindow.Avalonia.cs).
   Теперь все формы следуют одному правилу — акцентная кнопка подтверждения
   (заливка `AccentBrush`, контрастный текст `TextOnAccentBrush` по WCAG) и вторичная
   кнопка отмены (карточка + контур + текст темы).

4. **«И вот тут тоже бы поправить» + «Для сравнения — виндовое окно» (04:45).**
   Кнопка «ОК» маленького всплывающего окна на Linux красилась жёсткой зелёной
   заливкой (`#16A34A` + `Brushes.White`), а кнопка отмены — чёрным текстом на
   светлой подложке; на Windows те же диалоги уже выглядели тематическими. После
   унификации (п. 3) окно на обеих платформах рисуется одинаково: акцентная кнопка
   подтверждения и вторичная кнопка отмены — единый контракт 1.0.0.82.

5. **«Ещё одна новинка с зеброй» (05:03).**
   Чередование строк в таблицах переведено на тематическую кисть `ZebraRowBrush`:
   **#334155** (приглушённая slate-700) в тёмной теме и #F1F5F9 в светлой. Исправлены
   окна с `AlternationCount` без тематической строки (`ConfigTypeEditWindow`,
   `PortsEditWindow`), стили `DataGrid.RowStyle` с `AlternationIndex` на `ZebraRowBrush`
   и построчная «зебра» Avalonia
   ([`ActualReleasesWindow.xaml`](Workspace1C/Views/ActualReleasesWindow.xaml),
   [`PlatformUpdateWindow.xaml`](Workspace1C/Views/PlatformUpdateWindow.xaml),
   [`ConfigTypesEditWindow.Avalonia.cs`](Workspace1C/Views/ConfigTypesEditWindow.Avalonia.cs)
   и др.). Светлых/белых полос в тёмной теме больше нет.

Покрыто тестами-стражами [`Issue26ContrastToggleZebraGuardTests.cs`](Workspace1C.Tests/Issue26ContrastToggleZebraGuardTests.cs) (+13: контраст подписей главной по WCAG ≥ 4.5, переключатели не белые, диалоги без жёстких зелёных кнопок, тематическая «зебра»). Всего **4 908 юнит-тестов**.

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.102
