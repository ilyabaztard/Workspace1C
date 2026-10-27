using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Данные файла <c>infobase_notes.json</c>: плоский список заметок баз.
/// </summary>
public sealed class InfobaseNotesData
{
    public List<InfobaseNote> Notes { get; set; } = new();
}

/// <summary>
/// Хранилище заметок баз (п.8 «Заметки к базе», выпуск 1.0.0.90): файл
/// <c>infobase_notes.json</c> в каталоге данных профиля (через
/// <see cref="FeatureDataStore.DataDirectory"/>; тесты задают явный каталог).
/// Привязка — по ключу базы <see cref="InfobaseNote.InfobaseId"/> (ID 1С или имя, см.
/// <see cref="InfobaseNote.KeyFor"/>). Значения секретов хранятся зашифрованными
/// (см. <see cref="NoteCrypto"/>) — хранилище оперирует готовой моделью и значения
/// не расшифровывает. Битый файл не роняет функцию — читается как пустой список,
/// файл не перезаписывается до явного сохранения (как <see cref="FeatureDataStore"/>).
/// </summary>
public sealed class NoteStore
{
    /// <summary>Имя файла хранилища.</summary>
    public const string FileName = "infobase_notes.json";

    private readonly string? _directoryOverride;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true
    };

    private List<InfobaseNote>? _cache;

    /// <param name="directoryOverride">Явный каталог хранения (юнит-тесты); null — каталог данных профиля.</param>
    public NoteStore(string? directoryOverride = null)
    {
        _directoryOverride = directoryOverride;
    }

    /// <summary>Каталог хранения: переопределение (тесты) → каталог данных профиля.</summary>
    public string DataDirectory =>
        !string.IsNullOrWhiteSpace(_directoryOverride)
            ? _directoryOverride!
            : FeatureDataStore.DataDirectory;

    /// <summary>Полный путь к файлу хранилища.</summary>
    public string FilePath => Path.Combine(DataDirectory, FileName);

    /// <summary>Все заметки (читаются лениво и кэшируются на экземпляр).</summary>
    public IReadOnlyList<InfobaseNote> GetAll() => Load();

    /// <summary>Читает файл целиком; битый файл — пустой список (не роняет вызов).</summary>
    public List<InfobaseNote> Load()
    {
        if (_cache is not null)
            return _cache;
        try
        {
            if (!File.Exists(FilePath))
            {
                _cache = new List<InfobaseNote>();
                return _cache;
            }
            var data = JsonSerializer.Deserialize<InfobaseNotesData>(File.ReadAllText(FilePath), _jsonOptions);
            _cache = data?.Notes ?? new List<InfobaseNote>();
            return _cache;
        }
        catch
        {
            _cache = new List<InfobaseNote>();
            return _cache;
        }
    }

    /// <summary>Заметка базы по ключу или null.</summary>
    public InfobaseNote? Get(string infobaseId)
    {
        if (string.IsNullOrWhiteSpace(infobaseId))
            return null;
        return Load().FirstOrDefault(n => string.Equals(n.InfobaseId, infobaseId, StringComparison.Ordinal));
    }

    /// <summary>
    /// Сохраняет заметку базы (upsert по <see cref="InfobaseNote.InfobaseId"/>):
    /// проставляет <see cref="InfobaseNote.UpdatedAt"/> и пишет файл целиком.
    /// Пустая заметка без содержимого (нет markdown и секретов) — удаляется
    /// из хранилища, чтобы не копить «пустышки».
    /// </summary>
    public void Save(InfobaseNote note)
    {
        if (note is null || string.IsNullOrWhiteSpace(note.InfobaseId))
            return;

        var all = Load();
        var existingIndex = all.FindIndex(n => string.Equals(n.InfobaseId, note.InfobaseId, StringComparison.Ordinal));

        if (!note.HasContent)
        {
            if (existingIndex >= 0)
            {
                all.RemoveAt(existingIndex);
                SaveAll(all);
            }
            return;
        }

        note.UpdatedAt = DateTime.UtcNow;
        if (existingIndex >= 0)
            all[existingIndex] = note;
        else
            all.Add(note);
        SaveAll(all);
    }

    /// <summary>Удаляет заметку базы по ключу (нет записи — без изменений).</summary>
    public bool Delete(string infobaseId)
    {
        if (string.IsNullOrWhiteSpace(infobaseId))
            return false;
        var all = Load();
        var removed = all.RemoveAll(n => string.Equals(n.InfobaseId, infobaseId, StringComparison.Ordinal));
        if (removed > 0)
            SaveAll(all);
        return removed > 0;
    }

    /// <summary>
    /// Поиск по тексту: совпадение (без учёта регистра) в markdown-тексте карточки
    /// или в именах секретов. Значения секретов хранятся зашифрованными и в поиске
    /// не участвуют (поиск по шифртексту бессмыслен и раскрыл бы факт секрета).
    /// </summary>
    public IReadOnlyList<InfobaseNote> Search(string text)
    {
        return SearchNotes(Load(), text);
    }

    /// <summary>
    /// Удаляет записи заметок баз, которых больше нет в списке (ротация): вызывается
    /// после удаления базы. Не удаляет заметки временно отсутствующих баз (общие
    /// списки могут «прятать» базу на период синхронизации) — только явно.
    /// </summary>
    public int PurgeMissing(IEnumerable<string> existingKeys)
    {
        var keys = new HashSet<string>(existingKeys ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
        var all = Load();
        var kept = new List<InfobaseNote>();
        var removed = 0;
        foreach (var note in all)
        {
            if (note is not null && note.InfobaseId is not null && keys.Contains(note.InfobaseId))
                kept.Add(note);
            else
                removed++;
        }
        if (removed > 0)
        {
            _cache = kept;
            SaveAll(kept);
        }
        return removed;
    }

    /// <summary>
    /// Импортирует заметки из JSON-снимка (п.8, 1.0.0.90): переносится markdown-карточка
    /// и имена секретов (значения не переносятся — они зашифрованы ключом исходной машины).
    /// Локальная заметка той же базы имеет приоритет: если для ключа уже есть своя запись —
    /// перенос не перезаписывает её. Заметка без markdown-текста не создаётся.
    /// </summary>
    public int ImportTransferred(IEnumerable<InfobaseNoteTransfer>? transfers)
    {
        var imported = 0;
        if (transfers is null)
            return 0;
        foreach (var transfer in transfers)
        {
            if (transfer is null || string.IsNullOrWhiteSpace(transfer.InfobaseId))
                continue;
            if (Get(transfer.InfobaseId) is not null)
                continue; // локальная заметка важнее переносимой
            if (string.IsNullOrWhiteSpace(transfer.Markdown))
                continue;
            Save(new InfobaseNote
            {
                InfobaseId = transfer.InfobaseId,
                Markdown = transfer.Markdown,
                UpdatedAt = transfer.UpdatedAt == default ? DateTime.UtcNow : transfer.UpdatedAt
            });
            imported++;
        }
        return imported;
    }

    /// <summary>Пишет список заметок целиком (атомарно: tmp + Move, как FeatureDataStore).</summary>
    public void SaveAll(IReadOnlyList<InfobaseNote> notes)
    {
        var list = notes is List<InfobaseNote> l ? l : notes.ToList();
        var dir = DataDirectory;
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(new InfobaseNotesData { Notes = list }, _jsonOptions);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, FilePath, overwrite: true);
        _cache = list;
    }

    // ---------- Чистые функции (покрыты юнит-тестами без I/O) ----------

    /// <summary>Чистая версия поиска: совпадение по markdown или именам секретов.</summary>
    public static List<InfobaseNote> SearchNotes(IReadOnlyList<InfobaseNote>? notes, string? text)
    {
        var q = (text ?? string.Empty).Trim();
        if (notes is null)
            return new List<InfobaseNote>();
        if (q.Length == 0)
            return notes.Where(n => n is not null && n.HasContent).ToList();

        return notes
            .Where(n => n is not null)
            .Where(n =>
            {
                if (ContainsIgnoreCase(n.Markdown, q))
                    return true;
                foreach (var name in n.SecretNames)
                {
                    if (ContainsIgnoreCase(name, q))
                        return true;
                }
                return false;
            })
            .ToList();
    }

    /// <summary>Чистая версия upsert: объединяет заметки и сохраняет позицию существующей.</summary>
    public static List<InfobaseNote> MergeSave(IReadOnlyList<InfobaseNote>? all, InfobaseNote note)
    {
        var list = (all as List<InfobaseNote>) ?? (all?.ToList() ?? new List<InfobaseNote>());
        if (note is null || string.IsNullOrWhiteSpace(note.InfobaseId))
            return list;

        if (!note.HasContent)
        {
            list.RemoveAll(n => string.Equals(n?.InfobaseId, note.InfobaseId, StringComparison.Ordinal));
            return list;
        }

        var idx = list.FindIndex(n => string.Equals(n?.InfobaseId, note.InfobaseId, StringComparison.Ordinal));
        if (idx >= 0)
            list[idx] = note;
        else
            list.Add(note);
        return list;
    }

    /// <summary>Чистая версия ротации: оставляет только заметки существующих ключей.</summary>
    public static List<InfobaseNote> Purge(IReadOnlyList<InfobaseNote>? notes, IEnumerable<string>? existingKeys)
    {
        if (notes is null)
            return new List<InfobaseNote>();
        var keys = new HashSet<string>(existingKeys ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
        if (keys.Count == 0)
            return new List<InfobaseNote>();
        return notes.Where(n => n is not null && n.InfobaseId is not null && keys.Contains(n.InfobaseId)).ToList();
    }

    private static bool ContainsIgnoreCase(string? source, string value)
    {
        if (string.IsNullOrWhiteSpace(source))
            return false;
        return source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}

/// <summary>
/// Провайдер заметок для контекстов, где DI-сервис недоступен (простые помощники,
/// контракты поиска): гарантированно не падает и отдаёт пустые данные.
/// </summary>
public static class NoteStoreAccess
{
    private static NoteStore? _cached;

    /// <summary>Получает хранилище заметок: из контейнера приложений либо ленивый singleton.</summary>
    public static NoteStore GetOrCreate()
    {
        try
        {
            var fromDi = AppServices.TryGetService<NoteStore>();
            if (fromDi is not null)
                return fromDi;
        }
        catch
        {
            // DI ещё не настроен (тесты/CLI) — локальный экземпляр.
        }
        return _cached ??= new NoteStore();
    }
}
