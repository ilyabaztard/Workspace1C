# Workspace 1C 1.0.0.99

Релиз доступен для **Windows** (WPF) и **Linux** (Avalonia).
Скачать: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.99

### Исправлено

- **Миграция между кластерами 1С: в прогрессе имя обрабатываемой базы вместо
  предыдущей (issue #41, комментарий 7OH от 27.10)** — при обработке очередной базы
  строка «N из M: <база>» показывала имя ПРЕДЫДУЩЕЙ: «Во время обработки 2-й —
  показывает данные первой базы; во время начала обработки 3-й — надпись 2-й, и только
  в конце появилась 3-я».

  **Причина:** сеттеры `CurrentBaseName`/`ProcessedCount` не оповещали о смене
  `ProgressText` — окна (WPF и Avalonia) подписаны именно на событие `ProgressText`
  (обработчик `OnVmPropertyChanged` → `UpdateCommandStates` читает `vm.ProgressText`),
  поэтому строка пересобиралась при смене СЧЁТЧИКА (`ProgressIndex` поднимает событие),
  когда `CurrentBaseName` ещё содержал имя предыдущей базы; смена имени события
  `ProgressText` не поднимала, и корректное имя появлялось только на следующем
  оповещении (следующая итерация или сброс занятости в конце прогона).

  **Исправление:** `CurrentBaseName` и `ProcessedCount` теперь пересобирают
  `ProgressText` (общий ViewModel — обе платформы): имя обрабатываемой базы и счётчик
  «N из M» видны для каждой итерации в свой момент — первая, вторая (в т.ч. с ошибкой
  создания «базы нет в сервере СУБД»), третья
  ([`ClusterMigrationViewModel.cs`](Workspace1C/ViewModels/ClusterMigrationViewModel.cs)).

### Тесты

- [`ClusterMigrationViewModelTests.cs`](Workspace1C.Tests/ClusterMigrationViewModelTests.cs):
  +1 — регресс «оповещения прогресса несут имя КАЖДОЙ базы по очереди, включая вторую
  с ошибкой» (`Migrate_Progress_ShowsEachBaseNameInTurn_IncludingFailedSecond`): без
  фикса окно получало «2 из 3: База1» / «3 из 3: База2» и имени текущей базы не видело.

Подробности — в [CHANGELOG.md](https://github.com/sivatorov/Workspace1C/blob/main/CHANGELOG.md).
