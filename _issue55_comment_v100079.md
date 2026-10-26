Исправлено в версии 1.0.0.79 (Windows и Linux) по последнему комментарию («все те же формы как обрезались, так и обрезаются»):

1. **Адаптивная раскладка окон** — контент вкладок «Настройки» и «Настройка информационной базы» больше не обрезается при уменьшении окна: горизонтальная прокрутка включена как fallback (`HorizontalScrollBarVisibility=Auto`), жёсткие ширины полей и комбобоксов заменены на `MinWidth`, длинные подписи переносятся по словам (`Workspace1C/Views/SettingsWindow.xaml`, `Workspace1C/Views/SettingsWindow.Avalonia.cs`, `Workspace1C/Views/ConnectionSettingsWindow.xaml`).
2. **Аудит остальных окон** — ServerEdit, LaunchParameter, RacInfobaseCreate, RacInfobaseProperties, ScriptScenarioEdit (парные WPF/Avalonia): колонки подписей `Auto`+`MinWidth`, `ScrollViewer`, перенос текста, `TextTrimming` заголовка.

Покрыто тестами `Issue55AdaptiveLayoutTests` (+11).

Релиз: будет опубликован после сборки.