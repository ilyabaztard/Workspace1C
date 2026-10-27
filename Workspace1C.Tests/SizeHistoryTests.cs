using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты хранилища дневных снимков размеров (п.7 «История размеров баз и прогноз
/// диска», 1.0.0.89): ротация по MaxDays, запись/чтение, замена снимка того же дня,
/// фильтры GetHistory и агрегат GetLatest.
/// </summary>
public sealed class SizeHistoryTests : IDisposable
{
    private readonly string _dir;

    public SizeHistoryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "cm_size_history_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch { /* временный каталог — не критично */ }
    }

    private SizeHistoryStore NewStore() => new SizeHistoryStore(_dir);

    private static SizeSnapshotEntry Entry(
        string id, DateTime dateUtc, long size, long? copies = null, long? free = null)
        => new()
        {
            DateUtc = SizeHistoryStore.ToDayUtc(dateUtc),
            InfobaseId = id,
            InfobaseName = "База " + id,
            SizeBytes = size,
            CopiesBytes = copies,
            CopiesDiskFreeBytes = free,
            CopiesDrive = copies.HasValue ? "C:\\" : null
        };

    [Fact]
    public void Add_And_Load_PersistsSnapshot()
    {
        var store = NewStore();
        var now = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

        store.AddDailySnapshot(Entry("b1", now, 1_000), now);

        var reloaded = NewStore();
        var all = reloaded.GetAll();
        Assert.Single(all);
        Assert.Equal("b1", all[0].InfobaseId);
        Assert.Equal(SizeHistoryStore.ToDayUtc(now), all[0].DateUtc);
        Assert.Equal(1_000, all[0].SizeBytes);
    }

    [Fact]
    public void Add_SameDay_ReplacesEntry()
    {
        var store = NewStore();
        var now = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);

        store.AddDailySnapshot(Entry("b1", now, 100), now);
        store.AddDailySnapshot(Entry("b1", now, 250), now);

        var all = store.GetAll();
        Assert.Single(all);
        Assert.Equal(250, all[0].SizeBytes);
        Assert.Equal(SizeHistoryStore.ToDayUtc(now), all[0].DateUtc);
    }

    [Fact]
    public void Add_DifferentDays_KeepsBoth()
    {
        var store = NewStore();
        var day1 = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);
        var day2 = new DateTime(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc);

        store.AddDailySnapshot(Entry("b1", day1, 100), day1);
        store.AddDailySnapshot(Entry("b1", day2, 200), day2);

        Assert.Equal(2, store.GetAll().Count);
        Assert.Equal(2, store.GetHistory("b1").Count);
    }

    [Fact]
    public void Rotation_RemovesOlderThanMaxDays()
    {
        var store = NewStore();
        var now = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        // Ротация считается от самого позднего снимка: 40 дней старее всех — удаляется.
        store.AddDailySnapshot(Entry("b1", now.AddDays(-40), 100), now);
        store.AddDailySnapshot(Entry("b1", now.AddDays(-10), 200), now);
        store.AddDailySnapshot(Entry("b1", now, 300), now);

        var remain = SizeHistoryStore.Rotate(store.GetAll(), 30);
        Assert.Equal(2, remain.Count);
        Assert.DoesNotContain(remain, s => s.DateUtc == SizeHistoryStore.ToDayUtc(now.AddDays(-40)));
        Assert.Contains(remain, s => s.DateUtc == SizeHistoryStore.ToDayUtc(now));
        Assert.Contains(remain, s => s.DateUtc == SizeHistoryStore.ToDayUtc(now.AddDays(-10)));
    }

    [Fact]
    public void Rotation_MaxDaysZero_KeepsAll()
    {
        var now = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        var list = new List<SizeSnapshotEntry>
        {
            Entry("b1", now.AddDays(-500), 100),
            Entry("b1", now, 300)
        };

        var remain = SizeHistoryStore.Rotate(list, 0);
        Assert.Equal(2, remain.Count);
    }

    [Fact]
    public void Rotation_AppliesOnSave_AfterAdd()
    {
        var store = NewStore();
        var now = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        store.AddDailySnapshot(Entry("b1", now.AddDays(-100), 100), now, maxDays: 30);
        store.AddDailySnapshot(Entry("b1", now, 400), now, maxDays: 30);

        var reloaded = NewStore();
        Assert.Single(reloaded.GetAll());
        Assert.Equal(400, reloaded.GetAll()[0].SizeBytes);
    }

    [Fact]
    public void GetHistory_FiltersByRange()
    {
        var store = NewStore();
        var now = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        store.AddDailySnapshot(Entry("b1", now.AddDays(-60), 100), now);
        store.AddDailySnapshot(Entry("b1", now.AddDays(-20), 200), now);
        store.AddDailySnapshot(Entry("b1", now, 300), now);
        store.AddDailySnapshot(Entry("b2", now, 999), now);

        var from = SizeHistoryStore.PeriodFromUtc(now, 30);
        var history = store.GetHistory("b1", from);

        Assert.Equal(2, history.Count);
        Assert.All(history, s => Assert.True(s.DateUtc >= from));
        Assert.DoesNotContain(history, s => s.DateUtc == SizeHistoryStore.ToDayUtc(now.AddDays(-60)));
        Assert.DoesNotContain(history, s => s.InfobaseId == "b2");
    }

    [Fact]
    public void GetLatest_ReturnsNewestSnapshot()
    {
        var store = NewStore();
        var now = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        store.AddDailySnapshot(Entry("b1", now.AddDays(-5), 100), now);
        store.AddDailySnapshot(Entry("b1", now, 300), now);

        var latest = store.GetLatest("b1");
        Assert.NotNull(latest);
        Assert.Equal(300, latest?.SizeBytes);
        Assert.Null(store.GetLatest("missing"));
    }

    [Fact]
    public void HasSnapshot_DetectsDayPresence()
    {
        var store = NewStore();
        var now = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        store.AddDailySnapshot(Entry("b1", now, 100), now);

        Assert.True(store.HasSnapshot("b1", now));
        Assert.False(store.HasSnapshot("b1", now.AddDays(1)));
        Assert.False(store.HasSnapshot("b2", now));
    }

    [Fact]
    public void Add_IgnoredForEmptyId()
    {
        var store = NewStore();
        var now = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

        store.AddDailySnapshot(Entry("", now, 100), now);

        Assert.Empty(store.GetAll());
    }
}
