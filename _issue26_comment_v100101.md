Исправлено в версии **1.0.1.0** (Windows и Linux) по комментариям 7OH от 2026-10-28:

3. **«Синхронизация колонок — надписи по-прежнему чёрные».** Подписи в окне
   «Синхронизация» (Avalonia) брали дефолтный чёрный текст и в тёмной теме не
   читались. Теперь подписи полей привязаны к тематической кисти `FormLabelBrush`,
   а значение «последняя синхронизация» — к вторичной `TextSecondaryColorBrush`:
   контрастно и в тёмной, и в светлой теме
   ([`SettingsSyncWindow.Avalonia.cs`](Workspace1C/Views/SettingsSyncWindow.Avalonia.cs)).

4. **«Подсветка кнопок окна (свернуть/развернуть/закрыть) при неактивном окне —
   белым».** Подложка наведения кнопок окна в тёмной теме была «дневной»
   (#E2E8F0/#FFF3CD/#EDE7F6/#D6E4F0) и выглядела ярко-белой. Теперь метрики кнопок
   выбираются по темам через `ThemeFeatureMap.ButtonMetrics(style, dark)`, и в тёмной
   теме подложка наведения — полупрозрачный белый `#33FFFFFF` (как в Windows 11 /
   VS Code); «светофор» macOS и полупрозрачный Glass не трогаются
   ([`ThemeFeatureMap.cs`](Workspace1C/Themes/ThemeFeatureMap.cs),
   [`ThemeManager.cs`](Workspace1C/Themes/ThemeManager.cs),
   [`ThemeManager.Avalonia.cs`](Workspace1C/Themes/ThemeManager.Avalonia.cs)).

5. **«Половина надписей в одном окне белые, половина чёрные» + стрелки + чёрные
   надписи.** Во-первых, надписи окна «Синхронизация» унифицированы на контрастные
   кисти темы (п. 3), «половины чёрных/белых» больше нет. Во-вторых, стрелки
   сортировки заголовков DataGrid, которые собственным шаблоном WPF не рисуются,
   теперь выводятся тематическим глифом **▲/▼** кистью `TextSecondaryBrush` с
   триггерами `SortDirection` — видны и в тёмной, и в светлой теме
   ([`LightTheme.xaml`](Workspace1C/Themes/LightTheme.xaml),
   [`DarkTheme.xaml`](Workspace1C/Themes/DarkTheme.xaml)).

Покрыто тестами-стражами
[`Issue26ContrastToggleZebraGuardTests.cs`](Workspace1C.Tests/Issue26ContrastToggleZebraGuardTests.cs)
(+6: кисти подписей синхронизации и hover кнопок окна по метрикам тёмной темы,
глифы сортировки ▲/▼ в шаблонах DataGrid).

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.1.0