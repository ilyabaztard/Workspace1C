# Workspace 1C 1.0.0.96

Релиз доступен для **Windows** (WPF) и **Linux** (Avalonia).
Скачать: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.96

### Исправлено

- **При открытии окна «Серверы 1С» на Windows выбрасывался `InvalidCastException`
  (issue #75)** — при первом показе окна падал `AttachHeaderWidthTracking`
  ([`ServerMonitorWindow.xaml.cs`](Workspace1C/Views/ServerMonitorWindow.xaml.cs)):
  коллекция `presenter.Items` презентера заголовков в момент применения
  (ApplicationIdle) содержит не только `DataGridColumnHeader`, но и сырые колонки
  (`DataGridColumn`) — `Cast<DataGridColumnHeader>()` бросал «Specified cast is not
  valid». После ошибки вкладка «Соединения» могла не показывать список.

  Сбор заголовков теперь идёт безопасным `OfType<>` (лишние элементы пропускаются),
  плюс загрузка контейнеров заголовков, материализующихся позже, по
  `ItemContainerGenerator.StatusChanged`.

- **Перетаскивание колонок вкладок монитора серверов теперь как в 1С (issue #75,
  Windows и Linux)** — раньше при перетаскивании границы «двигалась одна-две
  колонки, упиралась в третью и всё» (звёздные колонки перераспределяли ширину на
  себя). Теперь:

  - **обычное перетаскивание** границы двигает ВСЕ колонки справа как блок — их
    ширины не меняются, меняются только позиции; при переполнении области
    появляется горизонтальная прокрутка и границу можно двигать дальше (в т.ч.
    если у первой колонки длинный заголовок);
  - **перетаскивание с Ctrl** меняет ширину ТОЛЬКО двух смежных колонок (левая
    растёт/ужимается, правая — зеркально), остальные колонки не двигаются;
  - колонки не сжимаются ниже минимума («заголовок + стрелка сортировки»);
  - математика вынесена в чистые функции
    [`ServerMonitorColumnWidthsStore`](Workspace1C/Services/ServerMonitorColumnWidthsStore.cs)
    (`MovedLeftColumnWidth`, `ResizedTwoAdjacentWidths`) — одна на обе платформы;
    WPF перехватывает штатную «ручку» `PART_RightHeaderGripper` заголовка DataGrid,
    Avalonia — собственную ручку шапки таблицы. Сохранение фактических ширин на
    вкладку (переживают закрытие программы) работает как раньше.

### Тесты

- [`ServerMonitorColumnWidthsTests.cs`](Workspace1C.Tests/ServerMonitorColumnWidthsTests.cs):
  четыре теста математики перетаскивания — обычный режим и Ctrl (сумма пары
  постоянна, минимумы обеих колонок соблюдены).
- [`Issue75ServerMonitorColumnsTests.cs`](Workspace1C.Tests/Issue75ServerMonitorColumnsTests.cs):
  `Wpf_HeaderTracking_UsesSafeTyping_NoInvalidCastException` — сбор заголовков через
  `OfType<>`, отсутствие опасного `Cast<>` по `presenter.Items`, перехват
  `PART_RightHeaderGripper`; `BothPlatforms_UseOneCStyleColumnDragSemantics` —
  обе платформы используют общую математику и перехват Ctrl. Всего **4862**
  юнит-теста (все проходят на Windows; Linux — 0 ошибок).
