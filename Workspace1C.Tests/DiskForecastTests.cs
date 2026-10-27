using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты чистых функций прогноза диска (п.7 «История размеров баз и прогноз диска»,
/// 1.0.0.89): линейная аппроксимация наименьших квадратов, темп роста, «дней до
/// заполнения» (в т.ч. краевые случаи) и подавление повторов оповещения.
/// </summary>
public sealed class DiskForecastTests
{
    private const long Gb = 1024L * 1024 * 1024;

    private static SizeSnapshotEntry Snapshot(
        DateTime dateUtc, long size, long? copies = null, long? free = null, string? drive = "C:\\")
        => new()
        {
            DateUtc = SizeHistoryStore.ToDayUtc(dateUtc),
            InfobaseId = "b1",
            SizeBytes = size,
            CopiesBytes = copies,
            CopiesDiskFreeBytes = free,
            CopiesDrive = copies.HasValue ? drive : null
        };

    [Fact]
    public void FitLinear_ExactLine_ReturnsSlopeAndIntercept()
    {
        var points = new[]
        {
            (0.0, 100.0),
            (1.0, 102.0),
            (2.0, 104.0),
            (3.0, 106.0)
        };

        var fit = DiskForecastService.FitLinear(points);

        Assert.True(fit.Valid);
        Assert.Equal(2.0, fit.Slope, precision: 6);
        Assert.Equal(100.0, fit.Intercept, precision: 6);
        Assert.Equal(1.0, fit.R2, precision: 6);
    }

    [Fact]
    public void FitLinear_FewerThanTwoPoints_Invalid()
    {
        Assert.False(DiskForecastService.FitLinear(new[] { (0.0, 1.0) }).Valid);
        Assert.False(DiskForecastService.FitLinear(Array.Empty<(double, double)>()).Valid);
        Assert.False(DiskForecastService.FitLinear(null!).Valid);
    }

    [Fact]
    public void FitLinear_ConstantY_HasZeroSlope()
    {
        var fit = DiskForecastService.FitLinear(new[]
        {
            (0.0, 500.0),
            (1.0, 500.0),
            (2.0, 500.0)
        });

        Assert.True(fit.Valid);
        Assert.Equal(0.0, fit.Slope, precision: 6);
    }

    [Fact]
    public void FitLinear_NoisyPoints_R2LessThanOne()
    {
        var fit = DiskForecastService.FitLinear(new[]
        {
            (0.0, 100.0),
            (1.0, 105.0),
            (2.0, 98.0),
            (3.0, 112.0)
        });

        Assert.True(fit.Valid);
        Assert.True(fit.Slope > 0);
        Assert.True(fit.R2 < 1.0);
    }

    [Fact]
    public void BuildPoints_SkipsInvalidAndDeduplicatesDays()
    {
        var now = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        var snapshots = new List<SizeSnapshotEntry>
        {
            Snapshot(now, 0, copies: 0),                       // невалидные (0) — пропуск
            Snapshot(now.AddDays(-2), 100, copies: 10),
            Snapshot(now.AddDays(-1), 200, copies: 20),
            Snapshot(now.AddDays(-1), 210, copies: 25),         // тот же день — последний
            Snapshot(now, 300, copies: 35)
        };

        var points = DiskForecastService.BuildPoints(snapshots, s => s.CopiesBytes);

        Assert.Equal(3, points.Count);
        Assert.Equal(0.0, points[0].X);
        Assert.Equal(35.0, points[2].Y);
        Assert.Equal(25.0, points[1].Y); // последний снимок дня -1
    }

    [Fact]
    public void EstimateGrowthRate_BasicPositive()
    {
        var now = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        var snapshots = new List<SizeSnapshotEntry>
        {
            Snapshot(now.AddDays(-4), 100 * Gb, copies: 10 * Gb, free: 200 * Gb),
            Snapshot(now.AddDays(-3), 101 * Gb, copies: 11 * Gb, free: 199 * Gb),
            Snapshot(now.AddDays(-2), 102 * Gb, copies: 12 * Gb, free: 198 * Gb),
            Snapshot(now.AddDays(-1), 103 * Gb, copies: 13 * Gb, free: 197 * Gb),
            Snapshot(now, 104 * Gb, copies: 14 * Gb, free: 196 * Gb)
        };

        var growth = DiskForecastService.EstimateGrowthRate(snapshots, s => s.CopiesBytes);

        Assert.NotNull(growth);
        Assert.Equal(1.0 * Gb, growth!.Value, precision: 0);
    }

    [Fact]
    public void EstimateGrowthRate_NoGrowthOrShrink_ReturnsNull()
    {
        var now = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        var flat = new List<SizeSnapshotEntry>
        {
            Snapshot(now.AddDays(-1), 100, copies: 50, free: 100),
            Snapshot(now, 100, copies: 50, free: 100)
        };
        Assert.Null(DiskForecastService.EstimateGrowthRate(flat, s => s.CopiesBytes));

        var shrinking = new List<SizeSnapshotEntry>
        {
            Snapshot(now.AddDays(-1), 100, copies: 60, free: 100),
            Snapshot(now, 100, copies: 40, free: 100)
        };
        Assert.Null(DiskForecastService.EstimateGrowthRate(shrinking, s => s.CopiesBytes));
    }

    [Fact]
    public void ForecastDaysUntilFull_Basic()
    {
        var now = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        var snapshots = new List<SizeSnapshotEntry>
        {
            Snapshot(now.AddDays(-3), 100 * Gb, copies: 10 * Gb, free: 30 * Gb),
            Snapshot(now.AddDays(-2), 101 * Gb, copies: 11 * Gb, free: 29 * Gb),
            Snapshot(now.AddDays(-1), 102 * Gb, copies: 12 * Gb, free: 28 * Gb),
            Snapshot(now, 103 * Gb, copies: 13 * Gb, free: 27 * Gb)
        };

        var forecast = DiskForecastService.ForecastDaysUntilFull(
            snapshots, 27 * Gb, s => s.CopiesBytes, now);

        Assert.True(forecast.Valid);
        // Рост 1 ГБ/день, свободно 27 ГБ → ~27 дней.
        Assert.Equal(27.0, forecast.DaysUntilFull, precision: 4);
        Assert.True(forecast.DaysMin >= 1 && forecast.DaysMin <= 27);
        Assert.True(forecast.DaysMax >= 27);
        Assert.NotNull(forecast.DateWhenFull);
        Assert.Equal(1.0 * Gb, forecast.GrowthPerDayBytes, precision: 0);
    }

    [Fact]
    public void ForecastDaysUntilFull_NoFreeDate_ReturnsNone()
    {
        var now = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        var snapshots = new List<SizeSnapshotEntry>
        {
            Snapshot(now.AddDays(-1), 100, copies: 10, free: null),
            Snapshot(now, 100, copies: 20, free: null)
        };

        var forecast = DiskForecastService.ForecastDaysUntilFull(snapshots, null, s => s.CopiesBytes, now);

        Assert.False(forecast.Valid);
    }

    [Fact]
    public void ForecastDaysUntilFull_NegativeFree_ReturnsNone()
    {
        var now = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        var snapshots = new List<SizeSnapshotEntry>
        {
            Snapshot(now.AddDays(-1), 100, copies: 10, free: 100),
            Snapshot(now, 100, copies: 20, free: 100)
        };

        var forecast = DiskForecastService.ForecastDaysUntilFull(snapshots, 0, s => s.CopiesBytes, now);

        Assert.False(forecast.Valid);
    }

    [Fact]
    public void ForecastDaysUntilFull_FewerThanTwoPoints_ReturnsNone()
    {
        var now = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        var single = new List<SizeSnapshotEntry> { Snapshot(now, 100, copies: 10, free: 100) };

        var forecast = DiskForecastService.ForecastDaysUntilFull(single, 100, s => s.CopiesBytes, now);

        Assert.False(forecast.Valid);
    }

    [Fact]
    public void ForecastDaysUntilFull_NoGrowth_ReturnsNone()
    {
        var now = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        var flat = new List<SizeSnapshotEntry>
        {
            Snapshot(now.AddDays(-1), 100, copies: 50, free: 100),
            Snapshot(now, 100, copies: 50, free: 100)
        };

        var forecast = DiskForecastService.ForecastDaysUntilFull(flat, 100, s => s.CopiesBytes, now);

        Assert.False(forecast.Valid);
    }

    [Fact]
    public void CheckThreshold_WithinThreshold_ProducesAlert()
    {
        var forecast = new DiskForecastResult(12.5, 10, 16, new DateTime(2026, 10, 30), 0.3 * Gb, 0.85, true);
        var lastSent = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        var now = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

        var alerts = DiskForecastService.CheckThreshold(forecast, "C:\\", 30, now, lastSent, repeatDays: 1);

        var key = DiskForecastService.DiskForecastAlertKey("C:\\");
        Assert.Single(alerts);
        Assert.NotEmpty(alerts[0]);
        // Вызов не мутирует lastSent (подавление применяется в цикле отправки).
        Assert.False(lastSent.ContainsKey(key));
    }

    [Fact]
    public void CheckThreshold_OutsideThreshold_NoAlert()
    {
        var forecast = new DiskForecastResult(80.0, 70, 90, new DateTime(2027, 1, 1), 0.1 * Gb, 0.9, true);

        var alerts = DiskForecastService.CheckThreshold(forecast, "D:\\", 30, DateTime.Now,
            new Dictionary<string, DateTime>(StringComparer.Ordinal), repeatDays: 1);

        Assert.Empty(alerts);
    }

    [Fact]
    public void CheckThreshold_ZeroThreshold_Disabled()
    {
        var forecast = new DiskForecastResult(5.0, 3, 8, new DateTime(2026, 10, 15), 0.1 * Gb, 0.9, true);

        var alerts = DiskForecastService.CheckThreshold(forecast, "C:\\", 0, DateTime.Now,
            new Dictionary<string, DateTime>(StringComparer.Ordinal), repeatDays: 1);

        Assert.Empty(alerts);
    }

    [Fact]
    public void CheckThreshold_SuppressesRepeatWithinPeriod()
    {
        var forecast = new DiskForecastResult(5.0, 3, 8, new DateTime(2026, 10, 15), 0.1 * Gb, 0.9, true);
        var key = DiskForecastService.DiskForecastAlertKey("C:\\");
        var now = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        var lastSent = new Dictionary<string, DateTime>(StringComparer.Ordinal) { [key] = now.AddHours(-1) };

        var alerts = DiskForecastService.CheckThreshold(forecast, "C:\\", 30, now, lastSent, repeatDays: 1);

        Assert.Empty(alerts); // подавлено: отправка была <1 дня назад

        var afterRepeat = now.AddDays(2);
        var alerts2 = DiskForecastService.CheckThreshold(forecast, "C:\\", 30, afterRepeat, lastSent, repeatDays: 1);
        Assert.Single(alerts2);
    }

    [Fact]
    public void CheckThreshold_InvalidForecast_NoAlert()
    {
        var alerts = DiskForecastService.CheckThreshold(DiskForecastResult.None, "C:\\", 30, DateTime.Now,
            new Dictionary<string, DateTime>(StringComparer.Ordinal), repeatDays: 1);

        Assert.Empty(alerts);
    }

    [Fact]
    public void FormatBytes_UsesHumanReadableUnits()
    {
        Assert.Equal("1 ГБ", DiskForecastService.FormatBytes(Gb));
        Assert.Equal("512 МБ", DiskForecastService.FormatBytes(512L * 1024 * 1024));
        Assert.Equal("1,5 ГБ", DiskForecastService.FormatBytes(1.5 * Gb));
    }
}
