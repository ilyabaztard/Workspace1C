# Workspace 1C 1.0.0.85

Релиз доступен для **Windows** (WPF) и **Linux** (Avalonia).
Скачать: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.85

### Добавлено

- **Регламентные задания информационных баз (меню «Утилиты» → «Операции» →
  «Регламентные задания ИБ…», Windows)** — новое окно управления регламентными
  заданиями без конфигуратора и без входа в базу. Слева — список баз с отметками и
  поиском, справа — задания выбранной базы: наименование, имя метаданных, признак
  «Включено», расписание, время и состояние последнего запуска, текст ошибки
  последнего запуска; сводка «всего / включено / с ошибкой». Кнопки **«Включить
  выбранное»** и **«Выключить выбранное»** меняют одно задание; **«Выключить все в
  отмеченных»** и **«Включить все в отмеченных»** обрабатывают все задания всех
  отмеченных баз по очереди со статусом по каждой базе. Поле **«Не трогать»** задаёт
  исключения по именам метаданных (кнопка «Выбранное → в исключения»).
  Работает через общий механизм COM во внешнем процессе Windows PowerShell
  (`ComScriptRunner`, 1.0.0.83) с учётными данными «Конфигуратора» базы; на Linux
  окно открывается с пояснением
  ([`ScheduledJobsService.cs`](Workspace1C/Services/ScheduledJobsService.cs),
  [`ScheduledJobsViewModel.cs`](Workspace1C/ViewModels/ScheduledJobsViewModel.cs),
  [`ScheduledJobsWindow.cs`](Workspace1C/Views/ScheduledJobsWindow.cs),
  [`ScheduledJobsWindow.Avalonia.cs`](Workspace1C/Views/ScheduledJobsWindow.Avalonia.cs)).
- **«Дублировать базу»: выключение регламентов в копии (Windows)** — после
  дублирования файловой или клиент-серверной базы программа предлагает сразу
  выключить в копии все регламентные задания, чтобы тестовая копия не выполняла обмены,
  не отправляла письма и не загружала данные
  ([`MainViewModel.Features.cs`](Workspace1C/ViewModels/MainViewModel.Features.cs),
  [`MainViewModel.Commands.cs`](Workspace1C/ViewModels/MainViewModel.Commands.cs)).

Покрыто тестами `ScheduledJobsServiceTests` (+12): разбор ответа (в т.ч. одиночный
объект вместо массива), отображаемое имя, операции скрипта, сводка, исключения,
логика окна, пункт меню; на Windows — реальный запуск PowerShell со скриптом заданий.

Подробности — в [CHANGELOG.md](https://github.com/sivatorov/Workspace1C/blob/main/CHANGELOG.md).
