# MCP-сервер Workspace 1C (`--mcp`)

**Выпуск 1.0.0.88** · п.6 «MCP-сервер и расширение CLI».

Режим `--mcp` превращает исполняемый файл `Workspace1C` в **MCP-сервер** (Model Context
Protocol) поверх **stdin/stdout**: ИИ-агенты (Claude Code, Roo Code и др.) получают
инструменты управления информационными базами 1С без открытия UI.

Протокол: **JSON-RPC 2.0** по stdio — каждая строка stdin одно сообщение, каждая строка
stdout один ответ. Никаких внешних NuGet-зависимостей (только `System.Text.Json`, реализация
своя — см. `Workspace1C/Services/Mcp/`).

```bash
# Windows
& "G:\…\Workspace1C.exe" --mcp
# Linux
/opt/workspace1c/Workspace1C --mcp
```

Сервер работает, пока открыт stdin; завершается по EOF. **В stdout пишутся ТОЛЬКО
JSON-RPC-сообщения** — вся диагностика уходит в stderr (без мусора в потоке протокола).

## Подключение к агенту

MCP-клиент запускает исполняемый файл с аргументом `--mcp` как stdio-сервер, например
в конфигурации Claude Desktop / Roo Code:

```json
{
  "mcpServers": {
    "workspace1c": {
      "command": "C:\\Tools\\Workspace1C.exe",
      "args": ["--mcp"]
    }
  }
}
```

Признак сервера — ответ на `initialize`:
`serverInfo.name = "Workspace 1C"`, `serverInfo.version = "1.0.0.88"`,
`capabilities.tools.listChanged = false`, `protocolVersion = "2024-11-05"`.

## Методы протокола

| Метод | Назначение | Ответ |
|---|---|---|
| `initialize` | Рукопожатие (версия, возможности, имя сервера) | `result` |
| `notifications/initialized` | Подтверждение инициализации (уведомление) | — |
| `ping` | Проверка живости | пустой `result` |
| `tools/list` | Список инструментов со схемами параметров | `result.tools[]` |
| `tools/call` | Вызов инструмента | `result.content[].text` (JSON данных) |

Ошибка инструмента возвращается **контентом с `isError: true`** (контракт MCP);
ошибка протокола — полем `error` JSON-RPC.

## Инструменты

### `list_infobases` — список баз
Параметры: `includePrivate` (boolean, опц.) — включать приватные базы разблокированного
профиля. Результат: `{ bases: [ { name, kind, id, group, connection, tags, isPrivate,
isFavorite, isPinned, lastLaunch, lastBackup } ], total }`. `kind`: `server`/`web`/`file`.

### `launch_infobase` — запуск базы
Параметры (обязательный `name`): `name` — имя или id базы; `designer` (boolean, опц.) —
режим «Конфигуратор». Результат: `{ launched: true, name, mode }`.
Приватные базы заблокированного профиля не запускаются.

### `clone_infobase` — копия клиент-серверной базы
Параметры (`source`, `server`, `databaseName` — обязательные):
`source` — имя/id исходной базы; `mode` — `full` (данные+конфигурация) или `config`
(только конфигурация); `cloneName`; `server` — сервер 1С копии; `databaseName` — имя
(Ref); `dbms`/`dbServer`/`dbName`/`dbUser`/`dbPassword` — параметры СУБД;
`createSqlDatabase`/`blockScheduledJobs` (boolean); `platformVersion`.
Результат: `{ cloned: true, mode, name, connection }` (строка подключения копии).
Механизм — «Дублировать базу» (`ServerCloneService`: DumpIB/DumpCfg → CREATEINFOBASE →
RestoreIB, время выполнения — минуты). **Операция безопасная, подтверждение не требуется.**

### `event_log_query` — журнал регистрации
Параметры (`name` **или** `path`): `name` — имя/id файловой базы; `path` — каталог
файловой ИБ (`1Cv8.1CD`); `from`/`to` — «yyyy-MM-dd[ HH:mm:ss]»; `user`, `event`, `text` —
подстроки фильтров; `limit` (по умолчанию 5000).
Результат: `{ location, format, truncated, count, entries: [ { timestamp, severity,
user, computer, application, event, metadata, comment, data, dataPresentation, session } ] }`.
Читаются оба формата журнала — SQLite (`.lgd`) и последовательный (`.lgf`/`.lgp`).

### `server_sessions` — сеансы сервера 1С
Параметры (обязательный `server`): `server` — id/описание/адрес записи справочника
«Серверы 1С»; `clusterId` (GUID, опц.) — кластер; `limit` (опц.).
Результат: `{ server, clusterId, total, count, sessions: [ { id, infobaseId, user, host,
appId, startedAt, lastActiveAt, state, blockedByLs, blockedByDeadlock, memoryBytes,
durationMs, bytesRead, bytesWritten, sessionConnectionId, hibernate } ] }` (данные rac
«session list»).

### `scheduled_jobs_state` — задания по расписанию
Параметры: нет. Результат: `{ count, jobs: [ { id, name, kind, enabled, time,
daysOfWeek, repeat, infobaseId, lastRunAt, lastRunSuccess, lastRunMessage } ] }`
(хранилище `IScheduledTaskStore`). **Изменение заданий через MCP не выполняется**
(только чтение состояния).

### `terminate_session` — ЗАВЕРШЕНИЕ сеанса (опасная операция)
Параметры (обязательные `server`, `sessionId`): `server` — запись справочника;
`sessionId` — GUID сеанса; `clusterId`/`processId` (GUID, опц.); `confirmTimeoutSeconds`
(по умолчанию 300).
**Требует подтверждения через Telegram**: перед выполнением в канал (настройки
«Уведомления: Telegram и email») отправляется вопрос с inline-кнопками «Да»/«Нет»
(`TelegramConfirmationService`, тот же механизм, что у `--telegram-confirm`). Без исхода
«Да» действие **не выполняется**; результат подтверждения включается в ответ.
Отказы: `denied`, `timeout`, `not_configured` (канал не настроен — операция заблокирована).

## Пример сеанса

Безопасный цикл (проверка, запуск, чтение журнала):

```bash
echo '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}' | Workspace1C --mcp
echo '{"jsonrpc":"2.0","id":2,"method":"tools/list"}' | Workspace1C --mcp
echo '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"list_infobases","arguments":{}}}' | Workspace1C --mcp
echo '{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"launch_infobase","arguments":{"name":"Бухгалтерия"}}}' | Workspace1C --mcp
echo '{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"event_log_query","arguments":{"name":"Бухгалтерия","from":"2026-10-27","limit":10}}}' | Workspace1C --mcp
```

Ответ tools/call — строка JSON вида:

```json
{"jsonrpc":"2.0","id":3,"result":{"content":[{"type":"text","text":"{\"bases\":[...],\"total\":3}"}],"isError":false}}
```

## Безопасность

- Опасные операции (завершение сеансов; в будущем — удаление базы, изменение
  расписаний) проходят через `McpSafetyGate` → `TelegramConfirmationService`
  (inline-кнопки, lock-файл «один активный запрос», ответы только из настроенного чата).
- В stdout — только JSON-RPC; диагностика и логи — в stderr и в файл журнала приложения.
- Сервер не требует GUI (Windows и Linux): для `server_sessions`/`terminate_session`
  нужен только исполняемый файл `rac` установленной платформы 1С.
- Справочник «Серверы 1С» для rac-инструментов заполняется в приложении.

## Архитектура

| Файл | Назначение |
|---|---|
| `Workspace1C/Services/Mcp/McpJsonRpc.cs` | Каркас JSON-RPC 2.0 (разбор, ответы, коды) |
| `Workspace1C/Services/Mcp/McpProtocolError.cs` | Коды ошибок протокола/инструментов |
| `Workspace1C/Services/Mcp/McpServer.cs` | stdio-цикл, диспетчеризация методов MCP |
| `Workspace1C/Services/Mcp/McpToolRegistry.cs` | Реестр инструментов (имена, схемы, описание) |
| `Workspace1C/Services/Mcp/McpToolHandlers.cs` | Реализации инструментов (сервисы приложения) |
| `Workspace1C/Services/Mcp/McpSafetyGate.cs` | Ворота подтверждения опасных операций |

Перехват `--mcp` — в `CliEntryPoint.TryHandle` (`Program.cs` обеих платформ вызывают его
**до** инициализации WPF/Avalonia), поэтому сервер работает и в окружении без DISPLAY.

Тесты: `Workspace1C.Tests/McpServerTests.cs` (JSON-RPC цикл, tools/list и схемы, tools/call
с ошибками, SafetyGate: подтверждено/отклонено/таймаут/не настроено),
`Workspace1C.Tests/CliMcpTests.cs` (разбор `--mcp`, `--update`, `--eventlog`, `--check`).