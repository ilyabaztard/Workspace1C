# Workspace 1C 1.0.1.2

Релиз доступен для **Windows** (WPF) и **Linux** (Avalonia).
Скачать: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.1.2

### Исправлено / Добавлено

- **Стрелки Up/Down в поле поиска командной палитры (issue #84, Windows и Linux).**
  Внутренняя обработка TextBox помечает стрелки Handled на bubbling-этапе, поэтому
  обработчик переведён на tunneling: WPF — `PreviewKeyDown`, Avalonia —
  `AddHandler(KeyDownEvent, Tunnel)`. Навигация — общая `MoveSelection` в
  `CommandPaletteViewModel` с уважением границ списка; фокус остаётся в поле
  поиска, выбор синхронизируется по индексу.

- **«Публикация на веб-сервере»: файл алиасов виден даже без детектора инстансов
  (issue #63, Windows и Linux).** При ручном указании/правке поля конфигурации
  строится «синтетический» инстанс из конфига
  (`WebPublishService.BuildInstanceFromConf`) через include-граф/Define
  (`ApacheInstanceDetector.PopulateIncludes`): в списке появляются главный конфиг
  и файл алиасов, каталог публикации и URL с портом берутся из конфига.

- **Отладка публикации (issue #63, Windows и Linux).** Новое направление
  логирования **CM_PUBLISH** (выключено по умолчанию, общий гейт CM_ON из #80):
  найденные инстансы (служба/конфиг/ServerRoot), причина fallback, include-граф,
  Define, найденные alias-файлы. Категория автоматически появилась на вкладке
  «Логирование» (WPF и Avalonia) с пояснением ru/en.

### Тесты

- [`CommandPaletteViewModelTests`](Workspace1C.Tests/CommandPaletteViewModelTests.cs)
  (+`MoveSelection`), новый
  [`CommandPaletteKeyNavigationTests`](Workspace1C.Tests/CommandPaletteKeyNavigationTests.cs)
  (стражи PreviewKeyDown/Tunnel и фокуса) — #84;
- [`WebPublishIssue63Tests`](Workspace1C.Tests/WebPublishIssue63Tests.cs)
  (+fallback на фикстурах 27_conf: ровно `1c8Aliases27.conf`,
  каталог `…\htdocs\27\demo_27`, URL `http://localhost:8027/demo_27/`), новый
  [`WebPublishPublishCategoryTests`](Workspace1C.Tests/WebPublishPublishCategoryTests.cs)
  (CM_PUBLISH) — #63;
- [`LoggingCategoriesTests`](Workspace1C.Tests/LoggingCategoriesTests.cs) — 8 направлений.

Итого в проекте **~5 001 юнит-тест**; Linux-сборка (Avalonia) также зелёная.

Подробности — в [CHANGELOG.md](https://github.com/sivatorov/Workspace1C/blob/main/CHANGELOG.md).
