#if !WINDOWS
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Workspace1C.Localization;
using Workspace1C.Services;

namespace Workspace1C
{
    public partial class SettingsWindow
    {
        /// <summary>Общий выключатель «CM_ON» вкладки «Логирование» (issue #80).</summary>
        private ToggleButton? _loggingMasterOnCheck;

        /// <summary>Соответствие «категория → переключатель» вкладки «Логирование» (issue #80).</summary>
        private readonly Dictionary<string, ToggleButton> _loggingCategoryChecks = new();

        /// <summary>
        /// Строит вкладку «Логирование» (issue #80 «Настройка логирования»): вводное
        /// пояснение, общий выключатель «CM_ON» и список категорий «как для колонок» —
        /// переключатель с названием и пояснением (HelpLink). По умолчанию все категории
        /// выключены; ERROR/WARN пишутся всегда. Значения читаются из <c>trace.json</c>
        /// через <see cref="TraceFlags"/>, изменения сохраняются туда же при «Сохранить»
        /// и применяются без перезапуска.
        /// </summary>
        private Control BuildLoggingTab()
        {
            var panel = new StackPanel { Spacing = 6 };

            panel.Children.Add(Hint(LocalizationManager.T("Settings.Logging.Description"), bottom: 10));

            // Общий выключатель «CM_ON».
            _loggingMasterOnCheck = SettingsSwitch(
                "Settings.Logging.MasterOn", TraceFlags.IsLoggingMasterOn(),
                helpTextKey: "Settings.Logging.MasterOnDesc");
            _loggingMasterOnCheck.Margin = new Thickness(0, 0, 0, 6);
            panel.Children.Add(_loggingMasterOnCheck);

            // Направления issue #80.
            panel.Children.Add(GroupSectionHeader("Settings.Logging.Directions"));
            AddLoggingCategories(panel, LogCategory.DirectionCategories);

            // Существовавшие до issue #80 диагностические флаги trace.json.
            panel.Children.Add(GroupSectionHeader("Settings.Logging.Diagnostics"));
            AddLoggingCategories(panel, LogCategory.DiagnosticCategories);

            return panel;
        }

        /// <summary>Добавляет строки-переключатели переданных категорий в панель вкладки.</summary>
        private void AddLoggingCategories(StackPanel panel, IEnumerable<string> categories)
        {
            foreach (var category in categories)
            {
                if (string.IsNullOrWhiteSpace(category))
                    continue;
                var toggle = SettingsSwitch(
                    LogCategory.TitleKey(category), TraceFlags.IsEnabled(category),
                    helpTextKey: LogCategory.DescriptionKey(category));
                // Строки опираются на пояснение в HelpLink, общий зазор панели не нужен.
                toggle.Margin = new Thickness(0);
                _loggingCategoryChecks[category] = toggle;
                panel.Children.Add(toggle);
            }
        }

        /// <summary>
        /// Сохраняет настройки вкладки «Логирование» в <c>trace.json</c> (issue #80):
        /// общий выключатель «CM_ON» и галочки направлений/диагностики. Записываются
        /// только известные флаги; рукописные значения конфига сохраняются.
        /// Применяется сразу — <see cref="TraceFlags"/> перечитывает конфиг при
        /// изменении файла, перезапуск не требуется.
        /// </summary>
        private void SaveLoggingSettingsAvalonia()
        {
            var values = new Dictionary<string, bool>
            {
                [LogCategory.MasterOn] = _loggingMasterOnCheck?.IsChecked == true
            };
            foreach (var pair in _loggingCategoryChecks)
                values[pair.Key] = pair.Value.IsChecked == true;
            TraceFlags.SaveCategorySettings(values);
        }
    }
}
#endif
