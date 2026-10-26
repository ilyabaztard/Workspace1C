Исправлено в версии 1.0.0.79 (Windows и Linux) по последнему комментарию («опять беда с контрастностью» — окно «Выбор родительской группы», подпись кнопки «Выбрать»):

1. **`TextOnAccentBrush` пересчитывается контрастной к фактическому акценту (WCAG ≥ 4.5)** вместо фиксированного белого — текст на акцентных кнопках больше не сливается с тёмно-янтарным акцентом пользовательских схем (`Workspace1C/Themes/ThemeManager.cs`, `Workspace1C/Themes/ThemeManager.Avalonia.cs`).
2. **Добавлена `SidebarTextBrush`** — контраст к фону тёмных панелей; тёмные панели переведены с `TextOnAccentBrush` на `SidebarTextBrush` (`Workspace1C/Views/MainWindow.xaml`, `Workspace1C/Views/MainWindow.Avalonia.cs`).
3. **Жёсткий `Foreground="White"`** на акцентных заливках заменён на `TextOnAccentBrush` (GroupPicker, MetadataExplorer, ConnectionSettings).

Покрыто тестами `ThemeContrastRegressionTests` (+14).

Релиз: будет опубликован после сборки.