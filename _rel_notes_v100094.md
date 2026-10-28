# Workspace 1C 1.0.0.94

Релиз доступен для **Windows** (WPF) и **Linux** (Avalonia).
Скачать: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.94

### Исправлено

- **«Движок выгрузки» в мастере «Развернуть тестовую копию» показывал CLR-имя
  типа вместо текста (issue #79)** — в выпадающем списке отображалось
  «Workspace1C.TestDeployWindow+ScheduleOptionValue» вместо «Конфигуратор»,
  «ibcmd (без окна)». Причина: пара «ключ/текст» для элементов списка не
  переопределяла `ToString()`. Теперь список построен на общем классе
  [`TestDeployEngineOption`](Workspace1C/Models/TestDeployEngineOption.cs)
  и используется в обеих платформах: WPF
  ([`TestDeployWindow.cs`](Workspace1C/Views/TestDeployWindow.cs)) и Avalonia
  ([`TestDeployWindow.Avalonia.cs`](Workspace1C/Views/TestDeployWindow.Avalonia.cs)).

### Тесты

- [`TestDeployTests.cs`](Workspace1C.Tests/TestDeployTests.cs): регрессия
  `EngineOption_ToString_ReturnsTextNotTypeName` (список движков показывает текст,
  а не CLR-имя типа) и `ViewModel_EngineIndex_MapsToDumpEngine` (выбор движка
  сохраняется в сценарий). Всего **4854** юнит-теста
  (все проходят на Windows; Linux — 0 ошибок).
