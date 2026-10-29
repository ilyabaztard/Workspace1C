Исправлено в версии **1.0.1.2** (Windows и Linux):

В поле поиска командной палитры клавиши Up/Down снова работают. Внутренняя
обработка WPF TextBox / Avalonia TextBox помечает стрелки Handled на
bubbling-этапе раньше, чем доходит обычный KeyDown, поэтому обработчик переведён
на tunneling-этап:

- WPF — `PreviewKeyDown`
  ([`CommandPaletteWindow.xaml`](Workspace1C/Views/CommandPaletteWindow.xaml),
  [`CommandPaletteWindow.xaml.cs`](Workspace1C/Views/CommandPaletteWindow.xaml.cs));
- Avalonia — `AddHandler(KeyDownEvent, Tunnel)`
  ([`CommandPaletteWindow.Avalonia.cs`](Workspace1C/Views/CommandPaletteWindow.Avalonia.cs)).

Навигация — единая для обеих платформ: `MoveSelection(int delta)` в
[`CommandPaletteViewModel`](Workspace1C/ViewModels/CommandPaletteViewModel.cs) — с
уважением границ списка; фокус остаётся в поле поиска, выбор в списке
синхронизируется по индексу.

Покрыто тестами
[`CommandPaletteViewModelTests.cs`](Workspace1C.Tests/CommandPaletteViewModelTests.cs)
(+`MoveSelection`) и новым
[`CommandPaletteKeyNavigationTests.cs`](Workspace1C.Tests/CommandPaletteKeyNavigationTests.cs)
(стражи PreviewKeyDown/Tunnel и фокуса).

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.1.2
