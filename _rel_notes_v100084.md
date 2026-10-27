# Workspace 1C 1.0.0.84

Релиз доступен для **Windows** (WPF) и **Linux** (Avalonia).
Скачать: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.84

### Добавлено

- **Движок ibcmd: выгрузка и расширения без «Конфигуратора» (Windows и Linux)** —
  утилита автономного сервера `ibcmd` (поставляется с платформой 8.3.14+ и ищется
  рядом с 1cv8 версии и разрядности базы) стала вторым «движком» операций с базой.
  Она работает быстрее, не открывает окон, не требует лицензии конфигуратора и
  одинаково ведёт себя в Windows и Linux.
  - **Сценарии резервирования** — в форме сценария появилось поле **«Движок
    выгрузки»**: «Конфигуратор (1cv8 DESIGNER)» (по умолчанию, как раньше) или
    «ibcmd (без конфигуратора)». Для форматов DT/ZIP/RAR выполняется
    `ibcmd infobase dump`, для CF — `ibcmd infobase config save`; дальше работают
    прежние упаковка, копирование по каталогам и ротация. Выбор движка действует и в
    заданиях по расписанию, использующих сценарий. Пустой файл результата считается
    ошибкой.
  - **Расширения конфигурации** — флажок **«Через ibcmd»** рядом с кнопкой установки:
    `.cfe` загружается и применяется командами `ibcmd infobase config load
    --extension` и `config apply --extension --force` во всех отмеченных базах.
  - **Подключение** — файловая база передаётся через `--db-path`; клиент-серверная —
    напрямую к СУБД (`--dbms --db-server --db-name --db-user --db-pwd`), параметры
    берутся из профиля базы в окне «Резервные копии СУБД» (без профиля — понятная
    подсказка). Пользователь ИБ — учётные данные «Конфигуратора» базы с учётом общей
    авторизации или явные учётные данные сценария. Аргументы передаются списком, без
    ручного экранирования; пароли маскируются в выводе и журнале команды.
  ([`IbcmdService.cs`](Workspace1C/Services/IbcmdService.cs),
  [`BackupEngine.cs`](Workspace1C/Models/BackupEngine.cs),
  [`BackupScenario.cs`](Workspace1C/Models/BackupScenario.cs),
  [`BackupService.cs`](Workspace1C/Services/BackupService.cs),
  [`BackupScenarioEditViewModel.cs`](Workspace1C/ViewModels/BackupScenarioEditViewModel.cs),
  [`BackupScenarioEditWindow.xaml`](Workspace1C/Views/BackupScenarioEditWindow.xaml),
  [`BackupScenarioEditWindow.Avalonia.cs`](Workspace1C/Views/BackupScenarioEditWindow.Avalonia.cs),
  [`ExtensionsViewModel.cs`](Workspace1C/ViewModels/ExtensionsViewModel.cs),
  [`ExtensionsWindow.cs`](Workspace1C/Views/ExtensionsWindow.cs),
  [`ExtensionsWindow.Avalonia.cs`](Workspace1C/Views/ExtensionsWindow.Avalonia.cs)).

Покрыто тестами `IbcmdServiceTests` (+14): аргументы файловой и клиент-серверной базы
(PostgreSQL/MS SQL, порт), отказ для веб-баз и без профиля СУБД, пользователь ИБ,
порядок аргументов и маскирование паролей, поиск ibcmd рядом с платформой, движок в
сценарии (по умолчанию «Конфигуратор», совместимость старых JSON) и в окне расширений.

Подробности — в [CHANGELOG.md](https://github.com/sivatorov/Workspace1C/blob/main/CHANGELOG.md).
