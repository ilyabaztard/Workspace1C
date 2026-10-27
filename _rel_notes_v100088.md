# Workspace 1C 1.0.0.88

Релиз доступен для **Windows** (WPF) и **Linux** (Avalonia).
Скачать: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.88

### Добавлено

- **MCP-сервер по stdio (п.6 «MCP-сервер и расширение CLI», Windows и Linux)** — новый
  headless-режим **`--mcp`**: Model Context Protocol поверх stdin/stdout (JSON-RPC 2.0,
  каждая строка — одно сообщение) без внешних зависимостей (чистый `System.Text.Json`).
  Перехват выполняется **до** инициализации UI (обе платформы — работает в окружении
  без DISPLAY); в stdout пишутся только JSON-RPC-сообщения, диагностика — в stderr.
  ИИ-агентам открываются инструменты:
  - **`list_infobases`** — список баз (имя, тип подключения server/web/file, id, группа,
    теги, приватность; аналог `--list`);
  - **`launch_infobase`** — запуск базы в «1С:Предприятии» или Конфигураторе
    (аналог `--run`; приватные базы заблокированного профиля не запускаются);
  - **`clone_infobase`** — копия клиент-серверной базы механизмом «Дублировать базу»
    (`DumpIB`/`DumpCfg` → `CREATEINFOBASE` → `RestoreIB`): полная копия (`full`) или
    только конфигурация (`config`), с параметрами СУБД и платформы;
  - **`event_log_query`** — журнал регистрации файловой базы (SQLite `.lgd` /
    последовательный `.lgf`/`.lgp`): цель — имя/id базы или каталог `1Cv8.1CD`,
    фильтры периода/пользователя/события/текста, лимит;
  - **`server_sessions`** — сеансы сервера 1С через `rac` по записи справочника
    «Серверы 1С» (id/описание/адрес), выбор кластера, лимит;
  - **`scheduled_jobs_state`** — состояние заданий по расписанию (тип, расписание,
    включение, результат последнего запуска);
  - **`terminate_session`** — **завершение сеанса (ОПАСНАЯ операция)**: выполняется
    ТОЛЬКО после подтверждения через Telegram (inline-кнопки «Да/Нет»,
    `TelegramConfirmationService` — тот же механизм, что у `--telegram-confirm`);
    без нажатия «Да» действие не выполняется, результат подтверждения включается в
    ответ (отказы: `denied` / `timeout` / `not_configured`).
  ([`McpServer.cs`](Workspace1C/Services/Mcp/McpServer.cs),
  [`McpToolRegistry.cs`](Workspace1C/Services/Mcp/McpToolRegistry.cs),
  [`McpToolHandlers.cs`](Workspace1C/Services/Mcp/McpToolHandlers.cs),
  [`McpSafetyGate.cs`](Workspace1C/Services/Mcp/McpSafetyGate.cs),
  [`McpJsonRpc.cs`](Workspace1C/Services/Mcp/McpJsonRpc.cs),
  [`CliEntryPoint.cs`](Workspace1C/Services/CliEntryPoint.cs)).
  Документация со схемами параметров и примерами — [`docs/mcp.md`](docs/mcp.md).
- **Расширение обычной командной строки (п.6)**:
  - **`--update [--apply]`** — проверка наличия новой версии через
    `GitHubReleaseService` (JSON: `updateAvailable`, `currentVersion`, `latestVersion`,
    `notes`, `downloadUrl`); при `--apply` — скачивание single-file сборки текущей
    платформы во временный каталог и запись маркера `pending_update.json`, чтобы
    следующая версия применилась при старте;
  - **`--eventlog --target <база/каталог файловой ИБ> [--from --to --user --event
    --text --limit]`** — выборка записей журнала регистрации с фильтрами; цель —
    имя/id файловой базы или каталог `1Cv8.1CD` (или сам каталог журнала);
    при `--json` — единый контракт `data.entries`;
  - **`--check`** — диагностика окружения/конфигурации: версия приложения, каталог
    данных, профиль, установленные платформы 1С (64/32 бита с каталогами `bin`),
    наличие `rac` и `ring`, число запущенных процессов 1С, статусы служб справочника
    «Управление службами» и повышены ли права текущего процесса;
  - **`--backup` / `--backup-all`** — проверены и задокументированы: резервное
    копирование одной/всех видимых баз (есть с 0.3.9.220), `--json` — единый контракт.
  ([`CliArgs.cs`](Workspace1C/Services/CliArgs.cs),
  [`CliCommands.cs`](Workspace1C/Services/CliCommands.cs),
  [`CliOutput.cs`](Workspace1C/Services/CliOutput.cs)).

### Обновлено

- [`docs/mcp.md`](docs/mcp.md) — новый раздел документации: протокол, методы, схемы
  инструментов, примеры сеанса, безопасность и архитектура; дополнен
  [`docs/telegram-dev-bridge.md`](docs/telegram-dev-bridge.md) (раздел 6: сводка новых
  команд и MCP) и [`README.md`](README.md) (командная строка).

### Тесты

- `McpServerTests` (+17): JSON-RPC 2.0 цикл (initialize → протокол/имя сервера/версия,
  ping, уведомления без ответа, ParseError/InvalidRequest/MethodNotFound/пустые строки),
  tools/list (все 6 инструментов + опасный `terminate_session`, схема параметров и
  обязательные поля), tools/call (инструмент не найден, без имени, ошибка инструмента)
  и поведение `McpSafetyGate` (подтверждено / отклонено / таймаут / Telegram не настроен,
  блокировка завершения сеанса без подтверждения).
- `CliMcpTests` (+13): разбор `--mcp` (с `--profile`), `--update` (+`--apply`),
  `--eventlog` (цель, фильтры, `--target=…`, отсутствие цели → ошибка, `--text`),
  `--check` и приоритет `--mcp`.
- Всего в наборе **4 748** тестов (0 не пройдено).

Подробности — в [CHANGELOG.md](https://github.com/sivatorov/Workspace1C/blob/main/CHANGELOG.md).