Исправлено в версии 1.0.0.82 (Windows и Linux) по последнему комментарию («Нужно зафиксировать минимальные высоту и ширину каждого окна и запретить дальнейшее уменьшение размера окна, если минимумы достигнуты»):

1. **Полный аудит минимальных размеров всех окон** — пройдены все окна приложения: каждому заданы `MinWidth`/`MinHeight`, при которых содержимое не обрезается (WPF — MinWidth/MinHeight в XAML, Avalonia — MinWidth/MinHeight в коде окна) (каталог `Workspace1C/Views/`).
2. **Закрыт обход минимумов в «Выборе баз»** — `BaseSelectionWindow` больше не сжимается меньше минимума на обеих платформах ([`BaseSelectionWindow.axaml`](Workspace1C/Views/BaseSelectionWindow.axaml)).
3. **Монитор серверов** — минимальная высота окна увеличена до 560, сужение меньше минимума запрещено ([`ServerMonitorWindow.xaml`](Workspace1C/Views/ServerMonitorWindow.xaml)).

Покрыто тестами `Issue55WindowLayoutTests`, `Issue55AdaptiveLayoutTests` (+6).

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.82