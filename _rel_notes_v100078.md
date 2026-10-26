# Workspace 1C 1.0.0.78

Релиз доступен для **Windows** (WPF) и **Linux** (Avalonia).

### Добавлено

- **Удаление из кластера: блокировка регламентных заданий перед завершением сеансов
  (issue #69, Windows и Linux)** — в диалог повтора добавлена галочка **«Установить
  блокировку регламентных заданий»**: при повторном нажатии «Завершить сеансы и
  повторить» сначала выполняется `infobase update` с `--scheduled-jobs-deny=on`
  ([`RacClient.cs`](Workspace1C/Services/RacClient.cs)), затем завершаются сеансы и
  соединения, затем повторяется `infobase drop`
  ([`RacInfobaseDropRetryWindow.xaml`](Workspace1C/Views/RacInfobaseDropRetryWindow.xaml),
  [`RacInfobaseDropRetryWindow.Avalonia.cs`](Workspace1C/Views/RacInfobaseDropRetryWindow.Avalonia.cs),
  [`DropRetryResult.cs`](Workspace1C/ViewModels/DropRetryResult.cs),
  [`ServerMonitorViewModel.cs`](Workspace1C/ViewModels/ServerMonitorViewModel.cs)).
  Локализация ru/en. Покрыто тестами `ServerMonitorViewModelTests`, `RacClientTests`,
  `Issue69DropRetryWindowLayoutTests`.

- **Миграция баз: сводка результатов «Перенесено: N · Ошибок: M» (issue #41, Windows и
  Linux)** — по завершении миграции итог успешных и ошибочных баз выводится в
  локализованной строке (ключ `ClusterMigration.Summary.ResultFormat`), ошибки одной
  базы не роняют финальную сводку
  ([`ClusterMigrationViewModel.cs`](Workspace1C/ViewModels/ClusterMigrationViewModel.cs),
  [`ClusterMigrationRow.cs`](Workspace1C/ViewModels/ClusterMigrationRow.cs),
  [`ClusterMigrationWindow.xaml`](Workspace1C/Views/ClusterMigrationWindow.xaml),
  [`ClusterMigrationWindow.axaml`](Workspace1C/Views/ClusterMigrationWindow.axaml)).
  Покрыто тестами `ClusterMigrationViewModelTests`, `ClusterMigrationServiceTests`.

### Исправлено

- **Удаление из кластера: неудачное завершение сеансов больше не прерывает цикл повтора
  (issue #69, Windows и Linux)** — при неудачном завершении сеансов диалог повтора
  показывается снова («Повторная попытка завершения сеансов не удалась…»), и
  пользователь может нажать «Завершить сеансы и повторить» ещё раз; «Отмена» прерывает
  цикл ([`ServerMonitorViewModel.cs`](Workspace1C/ViewModels/ServerMonitorViewModel.cs),
  [`ServerMonitorWindow.xaml.cs`](Workspace1C/Views/ServerMonitorWindow.xaml.cs),
  [`ServerMonitorWindow.Avalonia.cs`](Workspace1C/Views/ServerMonitorWindow.Avalonia.cs)).
  Покрыто тестами `ServerMonitorViewModelTests`, `Issue69DropRetryWindowLayoutTests`.

- **Публикация баз: файл алиасов снова в списке include-файлов ConfTargets (issue #63,
  Windows и Linux)** — детектор раскрывает Include/IncludeOptional-маску
  (`aliases/*.conf`) в реально существующие файлы
  ([`ApacheInstanceDetector.TryExpandWildcard`](Workspace1C/Services/ApacheInstanceDetector.cs));
  на случай, если include-каталог не захватывается парсером, добавлен fallback-поиск
  `1c8Aliases*.conf` рядом с конфигом службы
  ([`WebPublishViewModel.FindAliasFilesNear`](Workspace1C/ViewModels/WebPublishViewModel.cs)) —
  файл `1c8Aliases27.conf` снова появляется в ConfTargets окна публикации. Покрыто
  тестами `Apache27ConfRegressionTests`, `WebPublishIssue63Tests`,
  `ApacheConfIncludeParserTests`.

- **Миграция баз: индексы статуса привязаны к фактически обрабатываемой строке,
  continue-on-error (issue #41, Windows и Linux)** — прогресс больше не «уезжает» при
  снятии галочек и перезапуске: `ProgressIndex` вычисляется через `SourceRows.IndexOf + 1`,
  `ProgressTotal` — видимый список, добавлен счётчик `ProcessedCount`; ошибка переноса
  одной базы не прерывает миграцию — проблемная строка помечается `MigrationStatus` и
  сохраняет галочку, остальные выделенные базы обрабатываются; успешно перенесённые базы
  добавляются в группу приёмника по мере успеха
  ([`ClusterMigrationViewModel.cs`](Workspace1C/ViewModels/ClusterMigrationViewModel.cs),
  [`ClusterMigrationRow.cs`](Workspace1C/ViewModels/ClusterMigrationRow.cs)). Покрыто
  тестами `ClusterMigrationViewModelTests`, `ClusterMigrationServiceTests`.

### Подтверждено

- **Общая авторизация (issue #57)** — подтверждено пользователем («Есть - теперь
  раздельно»). Правок кода в этом релизе не потребовалось.

Подробности — в [CHANGELOG.md](https://github.com/sivatorov/Workspace1C/blob/main/CHANGELOG.md).
