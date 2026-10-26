Исправлено в версии 1.0.0.77 (Windows и Linux) по последнему комментарию (выделять тонкий клиент в списке платформ, как в «Настройках»):

1. **Тонкие клиенты в списке платформ окна «Сменить платформу»** — выделяются суффиксом «(тонкий клиент)» и оранжевой подсветкой, как в «Настройках» ([`PlatformDisplayFormatter.cs`](Workspace1C/Services/PlatformDisplayFormatter.cs), [`PlatformChangeViewModel.cs`](Workspace1C/ViewModels/PlatformChangeViewModel.cs), [`PlatformChangeWindow.xaml`](Workspace1C/Views/PlatformChangeWindow.xaml)).

Покрыто тестами `PlatformChangeViewModelTests`, `Issue54ThinClientHighlightTests`.

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.77
