# Workspace 1C 1.0.0.91

Релиз доступен для **Windows** (WPF) и **Linux** (Avalonia).
Скачать: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.91

### Добавлено

- **Синхронизация своих настроек между компьютерами (п.9 «Синхронизация настроек»,
  Windows и Linux)** — двусторонняя синхронизация личного списка баз, групп, тегов,
  сценариев, горячих клавиш и пользовательских действий через **WebDAV** или
  **сетевую папку**:
  - окно «Синхронизация настроек…» — кнопка в нижней панели «Настройки»; WPF
    [`SettingsSyncWindow.cs`](Workspace1C/Views/SettingsSyncWindow.cs) и Avalonia
    [`SettingsSyncWindow.Avalonia.cs`](Workspace1C/Views/SettingsSyncWindow.Avalonia.cs);
    ViewModel — [`SettingsSyncViewModel.cs`](Workspace1C/ViewModels/SettingsSyncViewModel.cs);
  - настройки хранилища [`SettingsSyncSettings.cs`](Workspace1C/Models/SettingsSyncSettings.cs)
    (`settings_sync.json`): тип (WebDAV/папка), адрес/путь, пользователь, пароль
    (шифруется через [`NoteCrypto.cs`](Workspace1C/Services/NoteCrypto.cs), как п.8), интервал
    автозапуска, набор файлов, момент последней синхронизации;
  - набор по умолчанию: `ibases.v8i` (персональный список 1С), `infobases.json`
    (список баз и теги), `groups.json`, `settings.json` (горячие клавиши), `custom_actions.json`,
    `custom_config_types.json`, `context_menu_layout.json`, `tray_menu_layout.json`,
    `utilities_menu_layout.json`, `deleted_groups.json`, `server_ports.json`,
    `scripts/scenarios/*.script.json`;
  - WebDAV-клиент расширен до двусторонней работы — GET/HEAD/PUT/MKCOL
    ([`WebDavClient.cs`](Workspace1C/Services/WebDavClient.cs)); существующий
    `WebDavUploader` (DbmsBackupService) не изменён;
  - ядро [`SettingsSyncService.cs`](Workspace1C/Services/Sync/SettingsSyncService.cs) +
    чистая логика плана [`SettingsSyncPlan.cs`](Workspace1C/Services/Sync/SettingsSyncPlan.cs):
    сравнение по времени изменения (**last-write-wins**), проигравшая версия
    сохраняется как `*.conflict-<дата>` в каталоге `settings_sync_conflicts` каталога
    данных профиля; пофайловый транспорт без «пинг-понга» (логические даты версий
    сохраняются в `_workspace1c_sync_meta.json` на удалённой стороне);
  - запуск: вручную (кнопка «Синхронизировать сейчас»), по интервалу (фоновый таймер
    [`SettingsSyncRunner.cs`](Workspace1C/Services/Sync/SettingsSyncRunner.cs)) и как
    задание планировщика нового типа «Синхронизация настроек»
    (`ScheduledTaskKind.SettingsSync` в окне «Задания по расписанию»);
  - безопасность: пароль WebDAV только зашифрованный, защита путей от выхода за
    каталог данных (zip-slip) в [`SettingsSyncPackage.cs`](Workspace1C/Services/Sync/SettingsSyncPackage.cs),
    секреты не попадают в журналы/результаты;
  - **ограничение (задокументировано)**: локальные пути каталогов баз могут отличаться
    между компьютерами — они переносятся как есть; на втором ПК пути правятся вручную
    либо файл исключается из списка.

### Тесты

- [`SettingsSyncTests.cs`](Workspace1C.Tests/SettingsSyncTests.cs): план операций
  last-write-wins (upload/download/skip, допуск сравнения дат), имена конфликтных
  резервов, безопасность путей (zip-slip) и zip-пакет round-trip, чтение/запись
  настроек, валидация хранилища, исполнение синхронизации по сетевой папке
  (выгрузка/скачивание/конфликт-бэкапы, отсутствие «пинг-понга»), шифрование пароля
  и его отсутствие в журнале, интервальная логика расписания.

Всего в релизе **4826** юнит-тестов (все проходят на Windows; Linux-сборка — без ошибок).