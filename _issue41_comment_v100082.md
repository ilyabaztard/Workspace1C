Исправлено в версии 1.0.0.82 (Windows и Linux) по последним комментариям («имя первой базы не показывает… отсортировать по имени… не закрывать окно»):

1. **Имя первой базы показывается сразу** — имя обрабатываемой базы и счётчик «N из M» устанавливаются ДО начала её обработки: для первой базы сразу видно «1 из 3: Имя базы», а не пустая строка ([`ClusterMigrationViewModel.cs`](Workspace1C/ViewModels/ClusterMigrationViewModel.cs)).
2. **Сортировка списка баз по имени** — список баз источника сортируется по имени при чтении (OrdinalIgnoreCase — стабильно и для русских имён) ([`MainViewModel.Tools.cs`](Workspace1C/ViewModels/MainViewModel.Tools.cs)).
3. **Окно не закрывается по завершении** — после миграции окно остаётся открытым: статусы строк (включая ошибки) остаются видимыми, результат накапливается, повторная миграция и закрытие — по кнопкам ([`ClusterMigrationWindow.xaml.cs`](Workspace1C/Views/ClusterMigrationWindow.xaml.cs), [`ClusterMigrationWindow.Avalonia.cs`](Workspace1C/Views/ClusterMigrationWindow.Avalonia.cs)).

Покрыто тестами `ClusterMigrationViewModelTests`, `Issue41WindowLayoutTests` (+5).

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.82