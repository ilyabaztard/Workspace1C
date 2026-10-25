Исправлено в версии 1.0.0.72 (Windows и Linux) — «Конфигуратор параметров запуска» (окно «Запустить с выбором параметров»):

1. **Правка параметров.** Добавлена кнопка **«Изменить»** (между «Добавить» и «Удалить»): выбранный пользовательский параметр редактируется в отдельном диалоге ([`LaunchParameterEditWindow.xaml`](Workspace1C/Views/LaunchParameterEditWindow.xaml) + [`LaunchParameterEditWindow.Avalonia.cs`](Workspace1C/Views/LaunchParameterEditWindow.Avalonia.cs)); дубли ключей отклоняются.
2. **Порядок параметров.** Кнопки **«Вверх»/«Вниз»** меняют порядок пользовательских параметров справочника (встроенные ключи каталога фиксированы) — чистая логика [`OneCLaunchArgumentParser.MoveCustomParameter`](Workspace1C/Services/OneCLaunchArgumentParser.cs).
3. **Поиск.** Поле поиска над списком фильтрует строки по ключу и описанию ([`OneCLaunchArgumentParser.FilterReferenceCatalog`](Workspace1C/Services/OneCLaunchArgumentParser.cs)).

Дополнительно пользовательские параметры справочника стали доступны и на Linux (Avalonia-окно получило панель «Добавить/Изменить/Вверх/Вниз/Удалить» и поиск; сохранение — через общий `CustomLaunchParameters`).

Локализация ru/en; добавлены юнит-тесты `LaunchParametersCatalogTests` (фильтр, перемещение, разбор записи, разметка окон).

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.72