using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Данные файла <c>size_history.json</c>: плоский список дневных снимков всех баз.
/// Ротация выполняется при каждом сохранении (<see cref="SizeHistoryStore.AddDailySnapshot"/>).
/// </summary>
public sealed class SizeHistoryData
{
    public List<SizeSnapshotEntry> Snapshots { get; set; } = new();
}

/// <summary>
/// Хранилище истории размеров баз по дням (п.7 «История размеров баз и прогноз диска»,
/// выпуск 1.0.0.89): файл <c>size_history.json</c> в каталоге данных профиля
/// (через <see cref="FeatureDataStore.DataDirectory"/>; тесты задают явный каталог).
/// Один дневной снимок на базу (повторное добавление в тот же день заменяет значение),
/// ротация по <see cref="DefaultMaxDays"/> дней (365). Чистые функции вынесены
/// статическими — юнит-тесты покрывают ротацию, запись/чтение и фильтры.
/// </summary>
public sealed class SizeHistoryStore
{
    /// <summary>Имя файла хранилища.</summary>
    public const string FileName = "size_history.json";

    /// <summary>Глубина истории по умолчанию (дней).</summary>
    public const int DefaultMaxDays = 365;

    /// <summary>Максимально допустимая глубина истории (защита от опечаток в настройках).</summary>
    public const int MaxAllowedDays = 3650;

    private readonly string? _directoryOverride;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private List<SizeSnapshotEntry>? _cache;
    private DateTime _cacheLoadedUtc;

    /// <param name="directoryOverride">Явный каталог хранения (юнит-тесты); null — каталог данных профиля.</param>
    public SizeHistoryStore(string? directoryOverride = null)
    {
        _directoryOverride = directoryOverride;
    }

    /// <summary>Каталог хранения: переопределение (тесты) → каталог данных профиля.</summary>
    public string DataDirectory =>
        !string.IsNullOrWhiteSpace(_directoryOverride)
            ? _directoryOverride!
            : FeatureDataStore.DataDirectory;

    /// <summary>Полный путь к файлу истории.</summary>
    public string FilePath => Path.Combine(DataDirectory, FileName);

    /// <summary>Все снимки (без срезов по дате/базе), отсортированные по дате.</summary>
    public IReadOnlyList<SizeSnapshotEntry> GetAll()
    {
        var all = Load();
        return all;
    }

    /// <summary>Читает файл целиком; битый файл — пустая история (не роняет вызов).</summary>
    public List<SizeSnapshotEntry> Load()
    {
        if (_cache is not null)
            return _cache;
        try
        {
            if (!File.Exists(FilePath))
            {
                _cache = new List<SizeSnapshotEntry>();
                _cacheLoadedUtc = DateTime.UtcNow;
                return _cache;
            }
            var data = JsonSerializer.Deserialize<SizeHistoryData>(File.ReadAllText(FilePath), _jsonOptions);
            _cache = data?.Snapshots
                ?? new List<SizeSnapshotEntry>();
            _cache.Sort((a, b) => a.DateUtc.CompareTo(b.DateUtc));
            _cacheLoadedUtc = DateTime.UtcNow;
            return _cache;
        }
        catch
        {
            _cache = new List<SizeSnapshotEntry>();
            return _cache;
        }
    }

    /// <summary>
    /// Добавляет дневной снимок базы: один снимок на (дата, база) — повтор в тот же день
    /// обновляет существующий, затем применяется ротация <paramref name="maxDays"/> и файл
    /// сохраняется. Возвращает true, если запись была изменена (добавлена/обновлена).
    /// </summary>
    public bool AddDailySnapshot(SizeSnapshotEntry entry, DateTime nowUtc, int maxDays = DefaultMaxDays)
    {
        if (entry is null || string.IsNullOrWhiteSpace(entry.InfobaseId))
            return false;

        var day = ToDayUtc(entry.DateUtc == default ? nowUtc : entry.DateUtc);
        entry.DateUtc = day;

        var all = Load();
        var existingIndex = all.FindIndex(s =>
            s.InfobaseId == entry.InfobaseId && s.DateUtc == day);
        if (existingIndex >= 0)
            all[existingIndex] = entry;
        else
            all.Add(entry);

        var rotated = Rotate(all, maxDays);
        all.Clear();
        all.AddRange(rotated);
        Save(all);
        return true;
    }

    /// <summary>Сохраняет список снимков (сортировка по дате).</summary>
    public void Save(IReadOnlyList<SizeSnapshotEntry> snapshots)
    {
        var list = snapshots is List<SizeSnapshotEntry> l
            ? l
            : snapshots.ToList();
        list.Sort((a, b) => a.DateUtc.CompareTo(b.DateUtc));

        var dir = DataDirectory;
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(new SizeHistoryData { Snapshots = list }, _jsonOptions);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, FilePath, overwrite: true);

        _cache = list;
        _cacheLoadedUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// История размеров базы в диапазоне дат (включительно, по дням UTC).
    /// Возвращает снимки, отсортированные по дате; null-параметры — без ограничения.
    /// </summary>
    public IReadOnlyList<SizeSnapshotEntry> GetHistory(
        string infobaseId, DateTime? fromUtc = null, DateTime? toUtc = null)
    {
        var from = fromUtc.HasValue ? ToDayUtc(fromUtc.Value) : (DateTime?)null;
        var to = toUtc.HasValue ? ToDayUtc(toUtc.Value) : (DateTime?)null;
        return Load()
            .Where(s => s.InfobaseId == infobaseId)
            .Where(s => from is null || s.DateUtc >= from)
            .Where(s => to is null || s.DateUtc <= to)
            .OrderBy(s => s.DateUtc)
            .ToList();
    }

    /// <summary>Последний снимок базы (по дате) или null.</summary>
    public SizeSnapshotEntry? GetLatest(string infobaseId)
    {
        return Load()
            .Where(s => s.InfobaseId == infobaseId)
            .OrderByDescending(s => s.DateUtc)
            .FirstOrDefault();
    }

    /// <summary>Есть ли снимок базы за указанный день (UTC)?</summary>
    public bool HasSnapshot(string infobaseId, DateTime dayUtc)
    {
        var day = ToDayUtc(dayUtc);
        return Load().Any(s => s.InfobaseId == infobaseId && s.DateUtc == day);
    }

    /// <summary>
    /// Чистая функция ротации: оставляет снимки не старше <paramref name="maxDays"/> дней
    /// от самого позднего дня в списке (по датам снимков). maxDays ≤ 0 — ротация не
    /// выполняется. Возвращает новый список, исходный не изменяется.
    /// </summary>
    public static List<SizeSnapshotEntry> Rotate(
        IReadOnlyList<SizeSnapshotEntry> snapshots, int maxDays)
    {
        if (snapshots is null || snapshots.Count == 0 || maxDays <= 0)
            return snapshots?.ToList() ?? new List<SizeSnapshotEntry>();

        var latest = snapshots.Max(s => s.DateUtc);
        var cutoff = latest.Date.AddDays(-(maxDays - 1)); // maxDays дней включая последний
        return snapshots
            .Where(s => s.DateUtc >= cutoff)
            .ToList();
    }

    /// <summary>Нормализует момент времени к началу суток по UTC (ключ дневного снимка).</summary>
    public static DateTime ToDayUtc(DateTime utc) => utc.Date;

    /// <summary>Начало периода «от» для диапазона в N дней, заканчивающегося сегодня (UTC).</summary>
    public static DateTime PeriodFromUtc(DateTime nowUtc, int days)
        => ToDayUtc(nowUtc).AddDays(-Math.Max(1, days) + 1);

    /// <summary>Сброс кэша загрузки (для тестов между сценариями).</summary>
    public void Invalidate() => _cache = null;
}
