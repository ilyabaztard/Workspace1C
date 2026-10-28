Исправлено в версии 1.0.0.96 (Windows и Linux):

1. **Ошибка при открытии окна «Серверы 1С» (InvalidCastException, две сразу)** —
   падал [`AttachHeaderWidthTracking()`](Workspace1C/Views/ServerMonitorWindow.xaml.cs):
   коллекция `presenter.Items` презентера заголовков DataGrid в момент применения
   (DispatcherPriority.ApplicationIdle) содержит НЕ только `DataGridColumnHeader`,
   но и сырые колонки (`DataGridColumn`) — `Cast<DataGridColumnHeader>()` бросал
   `System.InvalidCastException: Specified cast is not valid`. После ошибки вкладка
   «Соединения» могла не показывать список. Сбор заголовков теперь идёт безопасным
   `OfType<DataGridColumnHeader>()` (лишние элементы пропускаются), плюс «досъём»
   контейнеров по `ItemContainerGenerator.StatusChanged` — заголовки, которые
   материализуются позже (переключение вкладок), тоже подхватываются.

2. **Перетаскивание колонок теперь как в 1С** (обе платформы):
   - обычное перетаскивание границы двигает ВСЕ колонки справа как блок — их
     ширины не меняются, меняются только позиции; при переполнении области
     появляется горизонтальная прокрутка, границу можно двигать дальше (в т.ч.
     если у первой колонки длинный заголовок);
   - перетаскивание с зажатым Ctrl меняет ширину ТОЛЬКО двух смежных колонок
     (левая растёт/ужимается, правая — зеркально), остальные колонки не двигаются;
   - колонка не сжимается ниже минимума «заголовок + стрелка сортировки»;
   - математика вынесена в чистые функции
     [`ServerMonitorColumnWidthsStore.MovedLeftColumnWidth()`](Workspace1C/Services/ServerMonitorColumnWidthsStore.cs)
     и
     [`ResizedTwoAdjacentWidths()`](Workspace1C/Services/ServerMonitorColumnWidthsStore.cs) —
     одна реализация для WPF и Avalonia. WPF перехватывает штатную «ручку»
     `PART_RightHeaderGripper` заголовка DataGrid, Avalonia — собственную ручку
     шапки таблицы. Сохранение фактических ширин на вкладку (переживают закрытие
     программы) работает как раньше.

Покрыто регрессионными тестами: `Wpf_HeaderTracking_UsesSafeTyping_NoInvalidCastException`,
`BothPlatforms_UseOneCStyleColumnDragSemantics`
([`Issue75ServerMonitorColumnsTests.cs`](Workspace1C.Tests/Issue75ServerMonitorColumnsTests.cs)),
а также `MovedLeftColumnWidth_*`/`ResizedTwoAdjacentWidths_*`
([`ServerMonitorColumnWidthsTests.cs`](Workspace1C.Tests/ServerMonitorColumnWidthsTests.cs)).
Всего тестов: 4862 (все проходят на Windows; Linux — 0 ошибок).

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.96
