# Workspace 1C 1.0.0.98

Релиз доступен для **Windows** (WPF) и **Linux** (Avalonia).
Скачать: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.98

### Исправлено

- **Публикация на веб-сервере (issue #63, комментарий 7OH от 27.10: «Файл алиасов в
  списке не появился, как последствие — остальное тоже без изменений»)** — используемый
  файл алиасов `1c8Aliases27.conf` возвращён в список файлов для webinst на реальных
  конфигах из 27_conf.zip (`-d c:\Apache\Apache24 -f conf\httpd27.conf`):

  1. **Точный путь Include-строки проверяется напрямую.** Если Include-граф по
     какой-то причине не прочитал файл алиасов, но строка
     `Include "${SRVROOT}/conf/1c8Aliases${OneCPort}.conf"` раскрыта в полный путь
     `c:\Apache\Apache24\conf\1c8Aliases27.conf` (в т.ч. вне стандартных каталогов
     поиска — другой диск, `conf\vhosts\...`), VM берёт файл по точному пути
     Include-строки, а не только ищет имя файла рядом с конфигом
     ([`FindUsedAliasFileNear`](Workspace1C/ViewModels/WebPublishViewModel.cs)).
  2. **Include-шаблоны алиасов раскрываются по финальному словарю `Define`.**
     Цели `1c8Aliases*.conf`, запомненные на проходе с неполным словарём,
     пересобираются после стабилизации Include-графа — в путях fallback-кандидатов
     больше не встречаются сырые `${...}`
     ([`PopulateIncludes`](Workspace1C/Services/ApacheInstanceDetector.cs)).
  3. **Номер инстанса берётся из значений `Define`.** Суффикс «27» из
     `Define OneCPort 27` добавляется к кандидатам поиска `1c8Aliases<n>.conf`, даже
     когда DocumentRoot/Listen/Include-граф суффикс не дали; в инстансе сохраняется
     финальный Define-граф конфигурации
     ([`ApacheInstance.Defines`](Workspace1C/Models/ApacheInstance.cs)).

  Итог на конфигах 27_conf.zip: в списке файлов ровно один алиас —
  `1c8Aliases27.conf`, каталог публикации — `c:\Apache\Apache24\htdocs\27`,
  URL — `http://localhost:8027/...`, без сырых `${...}` в интерфейсе.

### Тесты

- [`Apache27ConfRegressionTests.cs`](Workspace1C.Tests/Apache27ConfRegressionTests.cs):
  +3 — end-to-end на реальных конфигах из 27_conf.zip с файлами на диске по точным
  путям (SRVROOT→временный каталог; include-файл читается графом; в ConfTargets ровно
  один `1c8Aliases27.conf`; каталог `…\htdocs\27` и порт/URL `8027` без сырых
  `${...}`), fallback по точному пути Include-строки вне стандартных каталогов,
  fallback по значению `Define OneCPort`. Всего **4884** юнит-теста (все проходят на
  Windows; Linux — 0 ошибок).
