Исправлено в версии 1.0.0.82 (Windows и Linux) по последнему комментарию («А вот файла нужного и как следствие портов и каталогов — нет»):

1. **Inline-комментарии в значениях директив** — при разборе конфигов Apache из значений директив отбрасываются inline-комментарии `# ...` (например, `Listen "80${OneCPort}" # порт` → `8027`); переменные `${...}` в `Include` раскрываются по полному Define-графу ([`ApacheConfIncludeParser.cs`](Workspace1C/Services/ApacheConfIncludeParser.cs), [`ApacheInstance.cs`](Workspace1C/Models/ApacheInstance.cs)).
2. **Fallback-поиск учитывает Include-шаблоны** — `FindUsedAliasFileNear` сверяет найденные файлы с именем из Include-строки (в т.ч. с `${...}`), поэтому в списке ConfTargets оказывается только фактически используемый файл алиасов — `1c8Aliases27.conf` ([`WebPublishViewModel.cs`](Workspace1C/ViewModels/WebPublishViewModel.cs), [`ApacheInstanceDetector.cs`](Workspace1C/Services/ApacheInstanceDetector.cs)).
3. **Корректные каталог публикации и порт/URL** — для найденного файла алиасов подставляются каталог `…/htdocs/27` и порт/URL `8027`, без сырых `${...}` в интерфейсе.

Покрыто тестами `ApacheConfIncludeParserTests`, `ApacheInstanceDetectorTests`, `Apache27ConfRegressionTests`, `WebPublishIssue63Tests` (+9).

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.82