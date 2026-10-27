using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// Заметки баз (п.8 «Заметки к базе», выпуск 1.0.0.90) — общая часть, доступная обеим
/// платформам: доступ к хранилищу <see cref="NoteStore"/>, текст заметки для поиска
/// (палитра команд/фильтр дерева) и очистка записей удалённых баз. Само окно «Заметки
/// базы» открывается платформенными файлами (WPF/Avalonia) — команда создаётся там же.
/// </summary>
public partial class MainViewModel
{
    private NoteStore? _notesStoreInstance;

    /// <summary>
    /// Хранилище заметок: единственный экземпляр на вью-модель; в DI приложение
    /// регистрирует свой синглтон (<c>NoteStoreAccess</c> вернёт его при настроенном
    /// контейнере). Никогда не бросает исключений — при недоступности возвращает
    /// локальный экземпляр в каталоге данных профиля.
    /// </summary>
    internal NoteStore GetOrCreateNotesStore() =>
        _notesStoreInstance ??= NoteStoreAccess.GetOrCreate();

    /// <summary>
    /// Заметка выбранной базы (null — заметки нет). Используется поиском и командами
    /// окна «Заметки»; ключ — <see cref="InfobaseNote.KeyFor"/>.
    /// </summary>
    internal InfobaseNote? NoteFor(Infobase? infobase)
    {
        var key = InfobaseNote.KeyFor(infobase);
        return string.IsNullOrEmpty(key) ? null : GetOrCreateNotesStore().Get(key);
    }

    /// <summary>
    /// Текст markdown-карточки базы для поиска (ограничен 2000 символов — чтобы
    /// большие заметки не замедляли фильтрацию pallet/tree). Пустая строка — заметки нет.
    /// </summary>
    internal string NoteSearchText(Infobase? infobase)
    {
        var note = NoteFor(infobase);
        if (note is null || string.IsNullOrWhiteSpace(note.Markdown))
            return string.Empty;
        var text = note.Markdown.Trim();
        return text.Length <= 2000 ? text : text.Substring(0, 2000);
    }

    /// <summary>
    /// Чистит заметки удалённых баз: вызывается при удалении базы из списка.
    /// Заметки баз, временно отсутствующих в списке (общие списки), не трогаются —
    /// ротация только по переданным ключам существующих баз.
    /// </summary>
    internal void PurgeNotesForMissingBases(IEnumerable<Infobase> existingBases)
    {
        try
        {
            var keys = existingBases?.Select(InfobaseNote.KeyFor)
                .Where(k => !string.IsNullOrEmpty(k)) ?? Enumerable.Empty<string>();
            GetOrCreateNotesStore().PurgeMissing(keys);
        }
        catch
        {
            // Чистка не должна ронять операцию удаления базы.
        }
    }
}
