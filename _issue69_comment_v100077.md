Исправлено в версии 1.0.0.77 (Windows и Linux) по последнему комментарию (упростить подсказку, увеличить окно, сохранять пароль, исправить ошибку повтора):

1. **Упрощена подсказка диалога повтора** — убрана фраза про завершение сеансов «во вкладке», осталось «Нажмите "Повторить попытку"» ([`RacInfobaseDropRetryWindow.xaml`](Workspace1C/Views/RacInfobaseDropRetryWindow.xaml)).
2. **Окно увеличено** — кнопки больше не обрезаются.
3. **Пароль сохраняется между попытками** — при повторном показе диалога передаётся сохранённый пароль, как и логин ([`ServerMonitorViewModel.cs`](Workspace1C/ViewModels/ServerMonitorViewModel.cs)).
4. **Исправлена ошибка rac 8.5.4 «Ошибка разбора параметра: process»** — `session terminate` / `connection disconnect` теперь передают `--process=<pid>` ([`IRacClient.cs`](Workspace1C/Services/IRacClient.cs), [`RacClient.cs`](Workspace1C/Services/RacClient.cs)).

Покрыто тестами `Issue69DropRetryWindowLayoutTests`, `ServerMonitorViewModelTests`, `RacClientTests`.

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.77
