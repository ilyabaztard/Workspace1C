using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.Services.EventLog;

/// <summary>
/// Выбор журнала регистрации для открытия окна (выпуск 1.0.0.69): у файловой базы —
/// каталог <c>1Cv8Log</c> рядом с <c>1Cv8.1CD</c>; у клиент-серверной базы журнал
/// лежит на сервере 1С (<c>srvinfo\reg_&lt;порт&gt;\&lt;id базы&gt;\1Cv8Log</c>),
/// поэтому пользователю предлагается указать каталог (локальный или сетевой).
/// </summary>
public static class EventLogLocationPicker
{
    /// <summary>
    /// Определяет журнал для базы или спрашивает каталог. Возвращает null, если
    /// пользователь отказался от выбора или каталог не содержит журнала
    /// (предупреждение уже показано).
    /// </summary>
    public static JournalLocation? Pick(Infobase? infobase, IDialogService dialogs)
    {
        var title = LocalizationManager.T("EventLog.Title");
        string? initialDir = null;

        if (infobase is not null && infobase.Connection.Type == ConnectionType.File)
        {
            var location = LgdJournalLocator.Resolve(infobase);
            if (location.IsValid)
                return location;
            initialDir = InfobaseMaintenanceService.GetFileBaseDirectory(infobase);
            dialogs.ShowWarning(
                string.Format(LocalizationManager.T("EventLog.NotFoundForBase"),
                    location.ErrorMessage ?? string.Empty),
                title);
        }
        else if (infobase is not null)
        {
            dialogs.ShowInfo(LocalizationManager.T("EventLog.ServerBaseHint"), title);
        }

        var folder = dialogs.OpenFolderDialog(LocalizationManager.T("EventLog.ChooseFolder"), initialDir);
        if (string.IsNullOrWhiteSpace(folder))
            return null;

        var picked = LgdJournalLocator.FromDirectory(folder);
        if (!picked.IsValid)
        {
            // Пользователь мог выбрать каталог базы, а не сам 1Cv8Log — пробуем вложенный.
            var nested = LgdJournalLocator.FromDirectory(
                System.IO.Path.Combine(folder, LgdJournalLocator.LogDirectoryName));
            if (nested.IsValid)
                return nested;
            dialogs.ShowWarning(picked.ErrorMessage ?? LocalizationManager.T("EventLog.NotFound"), title);
            return null;
        }
        return picked;
    }
}
