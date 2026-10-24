Исправлено в версии 1.0.0.71 (Windows и Linux).

Пройдены все формы; исправлены места, где тёмный/чёрный текст оставался нечитаемым в тёмной теме:

1. **Кисть текста на плашках избранного — `FavoriteTextBrush`.** Введена новая динамическая кисть, контрастная к `FavoriteColor` (рассчитывается по WCAG в [`ThemeManager.cs`](Workspace1C/Themes/ThemeManager.cs) и [`ThemeManager.Avalonia.cs`](Workspace1C/Themes/ThemeManager.Avalonia.cs)), объявлена в палитрах [`ColorScheme.cs`](Workspace1C/Models/ColorScheme.cs) и в обеих темах WPF [`LightTheme.xaml`](Workspace1C/Themes/LightTheme.xaml)/[`DarkTheme.xaml`](Workspace1C/Themes/DarkTheme.xaml) и Avalonia [`LightTheme.axaml`](Workspace1C/Themes/LightTheme.axaml)/[`DarkTheme.axaml`](Workspace1C/Themes/DarkTheme.axaml). Раньше текст на плашках был фиксированной константой `#1C1917`, нечитаемой у пользовательских схем с тёмным «янтарём».

2. **Бейдж «Избранное» правой панели главного окна.** Текст красился кистью `FavoriteBrush` — в тёмной теме это «жёлтое на жёлтом» (`#FBBF24` на плашке `#FEF3C7`), нечитаемо. Теперь тёмный текст через `FavoriteTextBrush`: [`MainWindow.xaml`](Workspace1C/Views/MainWindow.xaml:2451) и [`MainWindow.Avalonia.Tree.cs`](Workspace1C/Views/MainWindow.Avalonia.Tree.cs) (бейдж `themeBrushKey: "FavoriteTextBrush"`).

3. **Номера слотов Alt+N** (строка базы и «Горячие клавиши» в настройках): фиксированный `#1C1917` заменён на `{DynamicResource FavoriteTextBrush}` — [`MainWindow.xaml`](Workspace1C/Views/MainWindow.xaml:1801), [`SettingsWindow.xaml`](Workspace1C/Views/SettingsWindow.xaml:1732), [`MainWindow.Avalonia.Tree.cs`](Workspace1C/Views/MainWindow.Avalonia.Tree.cs:565) и [`SettingsWindow.Avalonia.cs`](Workspace1C/Views/SettingsWindow.Avalonia.cs:3201).

4. **Тексты окон, собираемых кодом** (окно публикации «пять полей», технологический журнал, резервное копирование СУБД, журнал регистрации, телеграм-бот и др.): [`AvaloniaUi`](Workspace1C/Views/AvaloniaUi.Avalonia.cs) `Text`/`Header`/`Cell` теперь явно привязывают кисти темы (`TextPrimaryBrush`/`TextSecondaryBrush`) — раньше они полагались на глобальный стиль `TextBlock` и могли оставаться чёрными на тёмном фоне. WPF-версия (`WpfUi.Text`) так и делала.

5. **Ревизия разметки.** Полный grep по `Views/*.xaml` и `Views/*.axaml`: фиксированных «почти чёрных» `Foreground` (`#1C1917`, `Black`, `#111827`, `#0F172A`, `#1E293B`) не осталось. Намеренно тёмные тексты (например подпись «Закреплено» `#5B21B6`) находятся только на постоянных светлых плашках (`#EDE9FE`) и читаемы в обеих темах.

Локализация не затронута — новых строк нет, ru/en не расходится.

Тесты-стражи (7 фактов): [`Issue61TitleColorsTests.cs`](Workspace1C.Tests/Issue61TitleColorsTests.cs) — бейджи избранного используют `FavoriteTextBrush`, в разметке обеих платформ нет фиксированных тёмных `Foreground`, кисть объявлена в палитрах и темах, `AvaloniaUi` привязывает кисти темы. Сборка успешна для Windows (WPF) и Linux (Avalonia).

Реализовано на обеих платформах.
