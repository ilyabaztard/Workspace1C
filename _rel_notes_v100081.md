# Workspace 1C 1.0.0.81

Релиз доступен для **Windows** (WPF) и **Linux** (Avalonia).
Скачать: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.81

### Исправлено

- **Журнал регистрации: база подставляется автоматически, выбор базы — вверху формы
  (issue #74, Windows и Linux)** — при открытии «Журнала регистрации» из Утилиты
  выбранная база подставляется автоматически (диалог выбора каталога больше не
  перебивает открытие), а вверху формы появился селектор базы: можно переключиться
  на любую другую базу из списка, журнал пересчитывается для неё; для
  клиент-серверных баз, чей журнал хранится на сервере 1С, остаётся кнопка
  «Открыть каталог…» для локальной копии
  ([`EventLogViewModel.cs`](Workspace1C/ViewModels/EventLogViewModel.cs),
  [`EventLogWindow.cs`](Workspace1C/Views/EventLogWindow.cs),
  [`EventLogWindow.Avalonia.cs`](Workspace1C/Views/EventLogWindow.Avalonia.cs),
  [`MainViewModel.Features.cs`](Workspace1C/ViewModels/MainViewModel.Features.cs),
  [`MainViewModel.Avalonia.Features.cs`](Workspace1C/ViewModels/MainViewModel.Avalonia.Features.cs)).
  Покрыто тестами `EventLogViewModelTests` (+7).

Полный прогон тестов зелёный (4552 тестов).

Подробности — в [CHANGELOG.md](https://github.com/sivatorov/Workspace1C/blob/main/CHANGELOG.md).