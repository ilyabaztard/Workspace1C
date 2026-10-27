Исправлено в версии 1.0.0.81 (Windows и Linux) — реализован предпочтительный вариант из описания: база подставляется автоматически, но её можно сменить селектором вверху формы:

1. **Автоподстановка базы при открытии из Утилиты** — «Журнал регистрации» открывается сразу с журналом выбранной базы, диалог выбора каталога больше не перебивает открытие ([`MainViewModel.Features.cs`](Workspace1C/ViewModels/MainViewModel.Features.cs), [`MainViewModel.Avalonia.Features.cs`](Workspace1C/ViewModels/MainViewModel.Avalonia.Features.cs)).
2. **Селектор базы вверху формы** — в заголовке окна появился выпадающий список баз: при выборе другой базы журнал пересчитывается для неё автоматически (заголовок и расположение журнала обновляются) ([`EventLogViewModel.cs`](Workspace1C/ViewModels/EventLogViewModel.cs), [`EventLogWindow.cs`](Workspace1C/Views/EventLogWindow.cs), [`EventLogWindow.Avalonia.cs`](Workspace1C/Views/EventLogWindow.Avalonia.cs)).
3. **Клиент-серверные базы** — их журнал хранится на сервере 1С, поэтому в окне остаётся кнопка «Открыть каталог…» для локальной копии журнала (прежний механизм выбора каталога сохранён).

Добавлены юнит-тесты `EventLogViewModelTests` (+7): автоподстановка выбранной базы, выбор первой базы без выделения, смена базы селектором (расположение и заголовок), клиент-серверная база, сортировка имён, ручной выбор каталога.

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.81