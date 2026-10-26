Исправлено в версии 1.0.0.80 (Windows и Linux) по последним комментариям («зачем мне все файлы алиасов — в файле подключён конкретный файл алиасов, один»):

1. **В списке ConfTargets — только используемый файл алиасов** — список файлов окна публикации содержит ровно тот файл, который подключён Include-строкой конфига службы (из Include-графа с раскрытием `${VAR}`, например `Include "${SRVROOT}/conf/1c8Aliases${OneCPort}.conf"` → `1c8Aliases27.conf`); fallback-поиск рядом с конфигом добавляет не более **одного** файла, совпадающего с Include-строкой ([`WebPublishViewModel.BuildConfTargets`](Workspace1C/ViewModels/WebPublishViewModel.cs), [`ApacheInstanceDetector.PopulateIncludes`](Workspace1C/Services/ApacheInstanceDetector.cs), [`ApacheConfIncludeParser`](Workspace1C/Services/ApacheConfIncludeParser.cs)).
2. **Корректный пересчёт каталога публикации и порта/URL** — при выборе инстанса и файла webinst каталог публикации берётся из `DocumentRoot` (`${SRVROOT}/htdocs/${OneCPort}` → `…/htdocs/27`), порт — из `Listen` (включая `Listen "80${OneCPort}"` → `8027`), URL — `http://localhost:<port>/<name>/`; значения по умолчанию — из конфига службы и его include-графа, а не из дефолтного `httpd.conf`.

Покрыто тестами `ApacheConfIncludeParserTests`, `Apache27ConfRegressionTests`, `WebPublishIssue63Tests`.

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.80