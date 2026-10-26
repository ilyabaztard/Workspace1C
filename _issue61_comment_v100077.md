Исправлено в версии 1.0.0.77 (Windows и Linux) по последнему комментарию (подсветка выбранной строки в «Конфигураторе параметров запуска»):

1. **Темизированный стиль `ModernListViewItem`** и неявные стили `ListView` и `GridViewColumnHeader` добавлены в обе темы WPF — подсветка строк в «Конфигураторе параметров запуска» и других окнах с ListView+GridView больше не «тёмная на тёмном» и не «белая на белом» ([`DarkTheme.xaml`](Workspace1C/Themes/DarkTheme.xaml), [`LightTheme.xaml`](Workspace1C/Themes/LightTheme.xaml), [`CustomActionsWindow.xaml`](Workspace1C/Views/CustomActionsWindow.xaml)).

Покрыто тестами `Issue61DarkPaletteAuditTests`, `Issue61TitleColorsTests`.

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.77
