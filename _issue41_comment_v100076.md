Исправлено в версии 1.0.0.76 (Windows и Linux) по последнему комментарию («Починить индексы; пропускать ошибки и пытаться обработать остальные выделенные»):

1. **Полоса прогресса на всю ширину окна** — индикатор растянут на всю ширину (Avalonia [`ClusterMigrationWindow.axaml`](Workspace1C/Views/ClusterMigrationWindow.axaml)), закреплено тестом `Issue41WindowLayoutTests`.
2. **Автообновление статусов приёмника после ошибки** — после неудачного переноса окно само перечитывает статусы приёмника, сразу видно, какие базы уже оказались в новом кластере ([`ClusterMigrationViewModel.MigrateAsync`](Workspace1C/ViewModels/ClusterMigrationViewModel.cs)).
3. **Имя группы «Хост - Кластер (порт)»** — порт добавляется только при нестандартном значении (не 1540/1541), при пустом хосте — просто имя кластера ([`ClusterMigrationService.BuildGroupName`](Workspace1C/Services/ClusterMigrationService.cs)).
4. **Дедупликация групп** — повторная миграция в тот же приёмник не создаёт группу-дубликат; ошибки на одной базе не мешают обработке остальных выделенных.

Покрыто тестами `ClusterMigrationServiceTests` (имя группы, дедупликация), `ClusterMigrationViewModelTests` (автообновление статусов, продолжение после ошибки), `Issue41WindowLayoutTests`.

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.76