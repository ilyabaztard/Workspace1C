namespace Workspace1C.Services;

/// <summary>
/// Платформонезависимая логика глобальной горячей клавиши «показать главное окно»
/// (issue #82): значение по умолчанию и валидация строки жеста. Используется
/// настройками, главным окном (регистрация) и юнит-тестами без UI-зависимостей.
/// </summary>
public static class GlobalHotkeyLogic
{
    /// <summary>
    /// Комбинация по умолчанию — Ctrl+Alt+W (свободная и легко запоминаемая,
    /// аналогично Start Manager). Указывается в локализации «Клавиши показывающие окно».
    /// </summary>
    public const string DefaultGesture = "Ctrl+Alt+W";

    /// <summary>
    /// Признак валидной строки жеста (разбирается HotkeyMatcher'ом). Для ГЛОБАЛЬНОЙ
    /// клавиши обязателен модификатор (Ctrl/Shift/Alt/Win): безусловный глобальный
    /// захват голой клавиши (пример: F5) перехватывал бы ввод в любом приложении.
    /// </summary>
    public static bool IsValid(string? gesture)
        => HotkeyMatcher.TryParse(gesture, out _, out var modifiers)
           && modifiers != HotkeyModifiers.None;

    /// <summary>
    /// Нормализует сохранённое значение: пустое/невалидное → пустое (не регистрировать),
    /// иначе — каноническая строка жеста.
    /// </summary>
    public static string Normalize(string? gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture))
            return string.Empty;
        var trimmed = gesture.Trim();
        return IsValid(trimmed) ? trimmed : string.Empty;
    }
}