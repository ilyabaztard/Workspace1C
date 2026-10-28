Исправлено в версии 1.0.0.94 (Windows и Linux):

1. **Список «Движок выгрузки» в мастере «Развернуть тестовую копию» показывал CLR-имя типа** вместо текста — в выпадающем списке отображалось «Workspace1C.TestDeployWindow+ScheduleOptionValue» вместо «Конфигуратор» / «ibcmd (без окна)». Причина: пара «ключ/текст» не переопределяла `ToString()`, и выпадающий список рендерил имя типа.
2. Теперь список движков построен на общем классе [`TestDeployEngineOption`](Workspace1C/Models/TestDeployEngineOption.cs) (`ToString() => Text`) и используется в обеих платформах: WPF ([`TestDeployWindow.cs`](Workspace1C/Views/TestDeployWindow.cs)) и Avalonia ([`TestDeployWindow.Avalonia.cs`](Workspace1C/Views/TestDeployWindow.Avalonia.cs)). Выбор движка по-прежнему сохраняется в сценарий (`DumpEngine`).

Покрыто регрессионными тестами `EngineOption_ToString_ReturnsTextNotTypeName` и `ViewModel_EngineIndex_MapsToDumpEngine` ([`TestDeployTests.cs`](Workspace1C.Tests/TestDeployTests.cs)).

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.94
