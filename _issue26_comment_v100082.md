Исправлено в версии 1.0.0.82 (Windows и Linux) по последнему комментарию («Может проще (и правильнее) сделать все кнопки единообразными по всему приложению?»):

1. **Единый контракт кнопок** — все кнопки переведены на единую систему стилей: `ModernButton` (акцентный фон + контрастный текст), `SecondaryButton` (вторичные действия) и новый `DangerButton` (опасные действия — «Удалить» и т.п.) ([`LightTheme.xaml`](Workspace1C/Themes/LightTheme.xaml), [`DarkTheme.xaml`](Workspace1C/Themes/DarkTheme.xaml), [`Controls.axaml`](Workspace1C/Themes/Controls.axaml)).
2. **Убраны жёсткие цвета** — из окон удалены локальные `#16A34A`, `#22C55E`, `#06B6D4`, `#DC2626`, `#EF4444` и собственные стили; текст на акцентных кнопках — контрастный `TextOnAccentBrush` ([`WpfUi.cs`](Workspace1C/Views/WpfUi.cs), [`ModalWindowBase.cs`](Workspace1C/Views/ModalWindowBase.cs)).
3. **Обновлено около 50 окон** в обеих платформах (WPF и Avalonia): семантика сохраняется — акцентная кнопка остаётся основным действием, опасные действия красные, вторичные — нейтральные, но стиль теперь единый по всему приложению.

Покрыто тестами `Issue26ButtonUniformityTests` (+9).

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.82