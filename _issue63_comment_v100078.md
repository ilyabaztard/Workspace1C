Исправлено в версии 1.0.0.78 (Windows и Linux) по последнему комментарию («файл алиаса по-прежнему не вернулся»):

1. **Файл алиасов снова в списке ConfTargets** — `1c8Aliases27.conf` возвращён в список файлов окна публикации: детектор раскрывает Include/IncludeOptional-маску (например `aliases/*.conf`) в реально существующие файлы, каждый файл попадает в IncludeFiles и в граф чтения ([`ApacheInstanceDetector.TryExpandWildcard`](Workspace1C/Services/ApacheInstanceDetector.cs)).
2. **Fallback-поиск файла алиасов** — если алиасы не попали в граф include, VM ищет `1c8Aliases*.conf` рядом с конфигом службы и добавляет его отдельным пунктом «Include: 1c8Aliases27.conf» без дубликатов ([`WebPublishViewModel.FindAliasFilesNear`](Workspace1C/ViewModels/WebPublishViewModel.cs)).

Покрыто тестами `Apache27ConfRegressionTests`, `WebPublishIssue63Tests`, `ApacheConfIncludeParserTests`.

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.78