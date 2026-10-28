# Workspace 1C 1.0.0.97

Релиз доступен для **Windows** (WPF) и **Linux** (Avalonia).
Скачать: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.97

### Исправлено

- **При удалении базы из кластера «Удаление из кластера» (issue #69, комментарии 7OH
  от 27.10: 19:44 / 19:53 / 20:49 / 20:59 с логами rac)** — удаление защищённой базы
  застревало: блокировка регламентных заданий не ставилась, сеансы/соединения не
  снимались, до самого «infobase drop» дело не доходило. По логам rac (комментарий
  20:59: `connection list` → `session list` → `connection disconnect` →
  «Служебное соединение не может быть удалено» → `infobase info` →
  «Недостаточно прав пользователя на информационную базу» → повторный `connection
  disconnect` → снова «Недостаточно прав…») разобраны четыре причины:

  1. **Креды администратора ИБ не передавались в разрыв соединений и завершение
     сеансов** — раньше `--infobase-user`/`--infobase-pwd` применялись только к
     самому «infobase drop». Для защищённой базы rac «connection disconnect»/
     «session terminate» возвращали «Недостаточно прав пользователя на
     информационную базу» («удалять сеансы не хочет», даже если блокировка РЗ уже
     стоит в свойствах базы). Теперь креды из диалога повтора передаются в
     [`TerminateSessionAsync`](Workspace1C/Services/RacClient.cs) и
     [`DisconnectConnectionAsync`](Workspace1C/Services/RacClient.cs)
     (`SessionTerminateArgs`/`ConnectionDisconnectArgs`), а
     [`ReleaseInfobaseActivityAsync`](Workspace1C/ViewModels/ServerMonitorViewModel.cs)
     пробрасывает их в каждый разрыв.

  2. **«Служебное соединение не может быть удалено» прерывало весь цикл разрыва** —
     в логе 7OH первый «connection disconnect» вернул эту ошибку, разрыв моментально
     прекращался, и до остальных соединений, сеансов и «infobase drop» дело не
     доходило. Служебное соединение нельзя разорвать, но drop оно НЕ блокирует —
     теперь такой отказ распознаётся
     ([`IsServiceConnectionError`](Workspace1C/ViewModels/ServerMonitorViewModel.cs)),
     соединение пропускается, разрыв остальных продолжается.

  3. **Блокировка регламентных заданий не ставилась на защищённой базе** — rac
     «infobase info» (догрузка параметров СУБД для «infobase update
     --scheduled-jobs-deny=on») и сам «infobase update» выполнялись БЕЗ кредов
     администратора ИБ → «Недостаточно прав», «Не удаётся теперь установить
     блокировку» (19:44). Теперь [`SetScheduledJobsBlockAsync`](Workspace1C/ViewModels/ServerMonitorViewModel.cs)
     и [`EnsureDbParametersAsync`](Workspace1C/ViewModels/ServerMonitorViewModel.cs)
     используют креды из диалога для обоих rac-вызовов.

  4. **Автообновление монитора не отключалось на время удаления** (20:49) — тик
     таймера перезапрашивал сеансы/соединения и мог пересоздавать соединения прямо
     во время разрыва. Теперь на время всей операции удаления (включая диалоги
     повтора) автообновление останавливается и восстанавливается после
     ([`DropSelectedInfobaseAsync`](Workspace1C/ViewModels/ServerMonitorViewModel.cs)).

### Тесты

- [`ServerMonitorViewModelTests.cs`](Workspace1C.Tests/ServerMonitorViewModelTests.cs):
  `DropSelectedInfobase_Retry_DisconnectAndTerminate_GetInfobaseCredentials`,
  `DropSelectedInfobase_ServiceConnectionDisconnectFailure_IsSkipped_DropSucceeds`,
  `DropSelectedInfobase_AutoRefresh_DisabledDuringOperation_RestoredAfter`,
  `DropSelectedInfobase_BlockScheduledJobs_PassesCredentials_ToInfobaseInfoAndUpdate`,
  `IsServiceConnectionError_DetectsServiceConnectionRejections`.
- [`RacClientTests.cs`](Workspace1C.Tests/RacClientTests.cs): `SessionTerminateArgs_*` /
  `ConnectionDisconnectArgs_*` — с кредами ИБ добавляются `--infobase-user`/`--infobase-pwd`,
  без кредов флагов нет.
- [`Issue69DropRetryWindowLayoutTests.cs`](Workspace1C.Tests/Issue69DropRetryWindowLayoutTests.cs):
  стражи источника — креды в разрыве, креды в блокировке РЗ, обработка служебных
  соединений, пауза автообновления. Всего **4881** юнит-тест
  (все проходят на Windows; Linux — 0 ошибок).
