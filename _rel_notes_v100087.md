# Workspace 1C 1.0.0.87

Релиз доступен для **Windows** (WPF) и **Linux** (Avalonia).
Скачать: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.87

### Добавлено

- **Мониторинг лицензий (п.5, окно «Серверы 1С» → вкладка «Лицензии», Windows и Linux)**:
  - **сколько лицензий занято на сервере и кем** — команда rac `session list --licenses`
    (новые поля моделей: тип лицензии `lic-type`, максимум `max-licenses`, занято
    `licenses`). Поддерживаются оба формата вывода rac: табличный (8.3.x, позиции
    колонок лицензий определяются по строке заголовка) и блочный «ключ : значение»
    (8.5.x). На вкладке — строки «пользователь — информационная база — хост — тип —
    занято — максимум — приложение»;
  - **какие программные лицензии установлены** — `ring license list` (Windows и Linux).
    На Linux, если ring отсутствует, используется утилита платформы
    `/opt/1cv8/<версия>/x86_64/utils/licensetool`; если недоступны и они — запасной
    источник: каталог файлов лицензий платформы (Windows —
    `%ProgramData%\1C\1Cv8\licenses`, Linux — `/etc/1cv8/License`) с предупреждением
    вкладки о запасном источнике;
  - сводка **«Всего: N, занято: N, свободно: N»** по установленным лицензиям и занятым
    сеансами (если утилиты лицензий недоступны — «Занято сеансами: N»).
  ([`RacOutputParser.cs`](Workspace1C/Services/RacOutputParser.cs),
  [`RacClient.cs`](Workspace1C/Services/RacClient.cs),
  [`RingLicenseService.cs`](Workspace1C/Services/RingLicenseService.cs),
  [`LicenseManager.cs`](Workspace1C/Services/LicenseManager.cs),
  [`ServerMonitorViewModel.cs`](Workspace1C/ViewModels/ServerMonitorViewModel.cs),
  [`ServerMonitorWindow.xaml`](Workspace1C/Views/ServerMonitorWindow.xaml)).
- **Порог «свободных лицензий осталось меньше N»** — настройка в окне «Уведомления в
  Telegram и по email» (секция «Лицензии»; N; **0 — проверка отключена**) и период
  повторных оповещений (по умолчанию — не чаще раза в **24 часа**, защита от спама).
  Проверка выполняется:
  - при загрузке данных кластера в мониторе серверов;
  - по расписанию мониторинга (окно «Мониторинг и оповещения») — параллельно с
    проверкой сеансов/памяти/блокировок.
  Оповещение уходит событием мониторинга в существующие каналы
  (`NotificationDispatcher`: системное уведомление, Telegram, email) при включённом
  фильтре «мониторинг» в настройках каналов.
  ([`NotificationChannelSettings.cs`](Workspace1C/Models/NotificationChannelSettings.cs),
  [`NotificationChannelsWindow.xaml`](Workspace1C/Views/NotificationChannelsWindow.xaml),
  [`ServerAlertsService.cs`](Workspace1C/Services/ServerAlertsService.cs)).

### Тесты

`LicenseTests` (+20): разбор `rac session list --licenses` (табличный по заголовку и
блочный 8.5.x; сеансы без лицензий не ломают разбор), разбор `ring license list`
(таблица с заголовком ru/en, свободный текст, мусор → пусто), расчёт сводки
«всего/занято/свободно» (в т.ч. недоступные установленные лицензии), логика порога
(0 — отключено, сообщение при «свободно < N», тихий пропуск при неизвестном объёме,
подавление повторов в N часов и повтор после истечения) и оповещение через
`LicenseManager` с защитой от спама. Всего в наборе **4 718** тестов.

Подробности — в [CHANGELOG.md](https://github.com/sivatorov/Workspace1C/blob/main/CHANGELOG.md).