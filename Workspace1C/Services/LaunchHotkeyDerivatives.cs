using System;
using System.Collections.Generic;

namespace Workspace1C.Services
{
    /// <summary>
    /// Производные горячие клавиши действий запуска (issue #48 «Запуск
    /// с аутентификацией»): «Запуск с выбором параметров» и «Запуск
    /// с аутентификацией» автоматически получают Ctrl+<основная> и
    /// Shift+<основная> от хоткея запуска режима (F3/F4 и т.п.).
    /// Чистый платформонезависимый класс: разбор строки жеста — через
    /// <see cref="HotkeyMatcher"/>, формат вывода соответствует HotkeyBox
    /// (Ctrl+Shift+Alt+Win + отображаемое имя клавиши).
    /// Если пользователь назначил основной хоткей с Shift или Ctrl, производные
    /// не создаются вовсе: Shift/Ctrl в основной комбинации уже означают
    /// «аутентификацию»/«параметры» и дублировать их бессмысленно.
    /// </summary>
    public static class LaunchHotkeyDerivatives
    {
        /// <summary>
        /// Вычисляет производные сочетания для основного хоткея запуска.
        /// </summary>
        /// <param name="baseHotkey">Основной хоткей запуска («F3», «Ctrl+F3», «Alt+F4», пусто).</param>
        /// <param name="withAuth">«Запуск с аутентификацией» — Shift + основной, или null.</param>
        /// <param name="withParams">«Запуск с выбором параметров» — Ctrl + основной, или null.</param>
        /// <returns>
        /// true, если производные созданы (хоть одна); false — если базовый хоткей
        /// не задан/невалиден или уже содержит Shift/Ctrl.
        /// </returns>
        public static bool TryDerive(string? baseHotkey, out string? withAuth, out string? withParams)
        {
            withAuth = null;
            withParams = null;

            if (!HotkeyMatcher.TryParse(baseHotkey, out var keyName, out var modifiers))
                return false;

            // Shift/Ctrl в основной комбинации заняты по смыслу производных
            // (Shift — аутентификация, Ctrl — параметры): не добавляем.
            if ((modifiers & (HotkeyModifiers.Shift | HotkeyModifiers.Control)) != 0)
                return false;

            var keyDisplay = DisplayKey(keyName);
            withAuth = Build("Shift", modifiers, keyDisplay);
            withParams = Build("Ctrl", modifiers, keyDisplay);
            return true;
        }

        /// <summary>
        /// Собирает сочетание в каноническом формате HotkeyBox: добавленный модификатор,
        /// затем существующие (Alt, Win) и имя клавиши. Порядок «Ctrl, Shift, Alt, Win»
        /// такой же, как у <code>HotkeyBox.FormatCombo</code>.
        /// </summary>
        private static string Build(string addedModifier, HotkeyModifiers existing, string keyDisplay)
        {
            var parts = new List<string>(4);

            // Существующие модификаторы не содержат Ctrl/Shift (проверено в TryDerive):
            // новый модификатор идёт первым, затем Alt/Win базовой комбинации.
            parts.Add(addedModifier);
            if ((existing & HotkeyModifiers.Alt) != 0) parts.Add("Alt");
            if ((existing & HotkeyModifiers.Windows) != 0) parts.Add("Win");
            parts.Add(keyDisplay);
            return string.Join("+", parts);
        }

        /// <summary>
        /// Отображаемое имя клавиши из канонического имени HotkeyMatcher/WPF
        /// (обратное отображение HotkeyBox.KeyToDisplay: «D0»→«0», «OemPlus»→«+» и т.п.).
        /// </summary>
        private static string DisplayKey(string keyName)
        {
            if (keyName.Length == 2 && keyName[0] == 'D' && keyName[1] >= '0' && keyName[1] <= '9')
                return keyName[1].ToString();

            if (keyName.StartsWith("NumPad", StringComparison.Ordinal)
                && keyName.Length == 7 && keyName[6] >= '0' && keyName[6] <= '9')
                return keyName;

            if (keyName.Length >= 2 && (keyName[0] == 'F' || keyName[0] == 'f')
                && int.TryParse(keyName.Substring(1), out var fNumber) && fNumber >= 1 && fNumber <= 24)
                return "F" + fNumber;

            return keyName switch
            {
                "OemComma" => ",",
                "OemPeriod" => ".",
                "OemQuestion" => "?",
                "OemPlus" => "+",
                "OemMinus" => "-",
                "OemOpenBrackets" => "[",
                "OemCloseBrackets" => "]",
                "OemQuotes" => "\"",
                "OemSemicolon" => ";",
                "OemBackslash" => "\\",
                "OemPipe" => "|",
                "OemTilde" => "~",
                "Add" => "NumPad+",
                "Subtract" => "NumPad-",
                "Multiply" => "NumPad*",
                "Divide" => "NumPad/",
                "Decimal" => "NumPad.",
                "Space" => "Space",
                "Escape" => "Esc",
                _ => keyName
            };
        }
    }
}