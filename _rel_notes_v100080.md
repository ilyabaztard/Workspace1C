# Workspace 1C 1.0.0.80

Релиз доступен для **Windows** (WPF) и **Linux** (Avalonia).
Скачать: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.80

### Добавлено

- **Публикация баз: в списке ConfTargets только используемый файл алиасов, корректные
  каталог публикации и порт (issue #63, Windows и Linux)** — список файлов окна
  публикации содержит только тот файл алиасов, который реально подключён в конфиге
  службы (взят из Include-графа с раскрытием `${VAR}`, например
  `Include "${SRVROOT}/conf/1c8Aliases${OneCPort}.conf"` → `1c8Aliases27.conf`),
  fallback-поиск рядом с конфигом добавляет не более одного файла; пересчёт каталога
  публикации (`DocumentRoot`) и порта/URL (`Listen`, включая `Listen "80${OneCPort}"` →
  `8027`) выполняется корректно при выборе инстанса и файла webinst; значения по
  умолчанию берутся из конфига службы и его include-графа
  ([`WebPublishViewModel.cs`](Workspace1C/ViewModels/WebPublishViewModel.cs),
  [`ApacheConfIncludeParser.cs`](Workspace1C/Services/ApacheConfIncludeParser.cs),
  [`ApacheInstanceDetector.cs`](Workspace1C/Services/ApacheInstanceDetector.cs)).
  Покрыто тестами `ApacheConfIncludeParserTests`, `Apache27ConfRegressionTests`,
  `WebPublishIssue63Tests`.

### Исправлено

- **Удаление из кластера: параметры СУБД гарантированно догружаются перед блокировкой
  регламентных заданий, неудача блокировки — строкой в диалоге повтора (issue #69,
  Windows и Linux)** — перед установкой блокировки регламентных заданий параметры СУБД
  (`dbms`/`db-server`/`db-name`) догружаются через `infobase info`, если их нет в сводке
  (ни `ArgumentException` «Не указан тип СУБД», ни пустой `--dbms=` в `infobase update`);
  неудача установки блокировки показывается строкой в диалоге повтора, а не отдельным
  окном, цикл повтора не прерывается; диалог повтора и предупреждения открываются поверх
  активного окна монитора серверов (`WindowOwnerResolver`)
  ([`ServerMonitorViewModel.cs`](Workspace1C/ViewModels/ServerMonitorViewModel.cs),
  [`RacClient.cs`](Workspace1C/Services/RacClient.cs),
  [`ServerMonitorWindow.xaml.cs`](Workspace1C/Views/ServerMonitorWindow.xaml.cs)).
  Покрыто тестами `ServerMonitorViewModelTests`, `RacClientTests`,
  `Issue69DropRetryWindowLayoutTests`.

Полный прогон тестов зелёный (4545 тестов).

Подробности — в [CHANGELOG.md](https://github.com/sivatorov/Workspace1C/blob/main/CHANGELOG.md).