Исправлено в версии 1.0.0.77 (Windows и Linux) по последнему комментарию (вернуть выбор файла алиасов, значения по умолчанию из конфига службы, расширить поле выбора сервера):

1. **Возвращён выбор include-файла алиасов (ConfTargets)** — в окне «Публикация на веб-сервере…» снова доступен выбор файла алиасов ([`WebPublishViewModel.cs`](Workspace1C/ViewModels/WebPublishViewModel.cs)).
2. **Значения по умолчанию берутся из конфига службы (`-f`), а не из дефолтного httpd.conf** ([`ApacheInstanceDetector.cs`](Workspace1C/Services/ApacheInstanceDetector.cs)).
3. **Поле выбора сервера расширено до 400px** — надписи больше не обрезаются ([`WebPublishWindow.cs`](Workspace1C/Views/WebPublishWindow.cs)).
4. **В подписи инстанса показывается имя конфига.**

Покрыто тестами `Apache27ConfRegressionTests`, `ApacheInstanceDetectorTests`, `WebPublishIssue63Tests`, `WebPublishServerComboBoxWidthTests`.

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.77
