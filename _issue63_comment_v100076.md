Исправлено в версии 1.0.0.76 (Windows и Linux) по последнему комментарию с приложенными конфигами ([27_conf.zip](https://github.com/user-attachments/files/33939318/27_conf.zip), строка запуска `-d c:\Apache\Apache24 -f conf\httpd27.conf`):

1. **Относительный `-f` резолвится от ServerRoot.** `-f conf\httpd27.conf` при `-d c:\Apache\Apache24` теперь раскрывается в `c:\Apache\Apache24\conf\httpd27.conf` — конфиг находится, а не «теряется» ([`ApacheInstanceDetector.cs`](Workspace1C/Services/ApacheInstanceDetector.cs)).
2. **Итеративный Include-граф по всему дереву.** Включаемые файлы раскрываются рекурсивно по всей цепочке (`httpd27.conf` → `httpd.conf` → `1c8Aliases27.conf`), включая вложенные Include из любых уровней.
3. **`Define` из любого файла графа.** Переменные, объявленные в любом включаемом файле, видны при раскрытии `DocumentRoot`/`Listen` независимо от порядка объявления.
4. **`DocumentRoot`/`Listen` ищутся в любом файле графа**, а не только в главном — публикации и порты из включаемых файлов попадают в список.
5. **Защита от сырых `${...}`.** Неразрешённые переменные в UI не попадают — вместо строки с `${...}` показывается раскрытое значение или ничего.

Регресс-тесты `Apache27ConfRegressionTests` на реальных конфигах из архива (фикстуры `TestData/Apache27Conf`: `httpd27.conf`, `httpd.conf`, `1c8Aliases27.conf`); обновлены `ApacheInstanceDetectorTests`.

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.76