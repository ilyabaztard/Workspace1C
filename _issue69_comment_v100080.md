Исправлено в версии 1.0.0.80 (Windows и Linux) по последним комментариям («этого диалога вообще быть не должно; тип СУБД в удаляемой базе явно же прописан», «диалог на заднем плане»):

1. **Параметры СУБД догружаются для блокировки РЗ** — если в сводке `summary list` нет `dbms`/`db-server`/`db-name`, они гарантированно догружаются через `infobase info` перед установкой блокировки регламентных заданий; никаких `ArgumentException` «Не указан тип СУБД» и пустого `--dbms=` в `infobase update` ([`ServerMonitorViewModel.SetScheduledJobsBlockAsync`](Workspace1C/ViewModels/ServerMonitorViewModel.cs), [`RacClient.BuildUpdateArgs`](Workspace1C/Services/RacClient.cs)).
2. **Диалога об ошибке блокировки больше нет** — неудача установки блокировки показывается строкой в диалоге повтора (а не отдельным окном), цикл повтора не прерывается; удаление стабильнее ([`ServerMonitorViewModel.DropSelectedInfobaseAsync`](Workspace1C/ViewModels/ServerMonitorViewModel.cs), [`ServerMonitorWindow.xaml.cs`](Workspace1C/Views/ServerMonitorWindow.xaml.cs)).
3. **Диалоги поверх активного окна** — диалог повтора и предупреждения открываются поверх активного окна монитора серверов (`WindowOwnerResolver`), WPF и Avalonia.

Покрыто тестами `ServerMonitorViewModelTests`, `RacClientTests`, `Issue69DropRetryWindowLayoutTests`.

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.80