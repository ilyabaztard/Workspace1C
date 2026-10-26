Исправлено в версии 1.0.0.78 (Windows и Linux) по последнему комментарию (починить индексы; пропускать ошибки и обрабатывать остальные выделенные):

1. **Статус привязан к фактически обрабатываемой строке** — `ProgressIndex` считается через `SourceRows.IndexOf(row)+1`, `ProgressTotal` — по видимому списку (стабилен при снятии галочек), добавлен счётчик `ProcessedCount`; `CurrentBaseName` сбрасывается в начале прогона и обновляется только на фактическую строку ([`ClusterMigrationViewModel.cs`](Workspace1C/ViewModels/ClusterMigrationViewModel.cs), [`ClusterMigrationRow.cs`](Workspace1C/ViewModels/ClusterMigrationRow.cs)).
2. **Continue-on-error** — ошибка одной базы не прерывает миграцию: проблемная строка помечается статусом ошибки и сохраняет галочку, обработка переходит к следующей; успешные строки снимают галочку по мере переноса.
3. **«Есть в кластере, но нет в списке» устранено** — успешно перенесённые базы добавляются в список по мере успеха; если перенесена хотя бы одна база — результат применяется, даже при ошибках на других строках.
4. **Сводка «Перенесено: N · Ошибок: M»** — итоговая строка по ключу `ClusterMigration.Summary.ResultFormat` (локализация ru/en); автообновление статусов приёмника после ошибки сохранено ([`ClusterMigrationWindow.xaml`](Workspace1C/Views/ClusterMigrationWindow.xaml), [`ClusterMigrationWindow.axaml`](Workspace1C/Views/ClusterMigrationWindow.axaml)).

Покрыто тестами `ClusterMigrationViewModelTests`, `ClusterMigrationServiceTests`.

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.78