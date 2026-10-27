using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>Точка линейного графика в нормализованных координатах 0..1 (X — дата, Y — размер).</summary>
public sealed record SizeChartPoint(double X, double Y, string DateLabel, long SizeBytes);

/// <summary>
/// Данные графика роста размера ИБ (п.7, 1.0.0.89): нормализованные точки 0..1 и
/// подписи осей. Строится без UI-зависимостей — оба окна (WPF/Avalonia) масштабируют
/// точки в свои пиксельные размеры и рисуют полилинию.
/// </summary>
public sealed class SizeChartData
{
    public required IReadOnlyList<SizeChartPoint> Points { get; init; }

    public required string YMaxLabel { get; init; }

    public required string YMinLabel { get; init; }

    public required string XMinLabel { get; init; }

    public required string XMaxLabel { get; init; }

    public static readonly SizeChartData Empty = new SizeChartData
    {
        Points = Array.Empty<SizeChartPoint>(),
        YMaxLabel = "",
        YMinLabel = "",
        XMinLabel = "",
        XMaxLabel = ""
    };
}

/// <summary>
/// Построение данных графика роста размера ИБ по дневным снимкам (п.7, выпуск 1.0.0.89).
/// Чистые функции: вход — снимки, выход — нормализованные точки 0..1 и подписи осей;
/// рисование выполняют окна (без внешних графических библиотек).
/// </summary>
public static class SizeHistoryChartModel
{
    /// <summary>
    /// Строит график по снимкам размера ИБ (SizeBytes). Менее 2 валидных точек — null.
    /// Y — размер в байтах (метка — человекочитаемая), X — дни от первого снимка.
    /// </summary>
    public static SizeChartData? Build(IReadOnlyList<SizeSnapshotEntry> snapshots)
    {
        var valid = new List<(DateTime Date, long Size)>();
        if (snapshots is not null)
        {
            foreach (var s in snapshots)
            {
                if (s is null || !s.SizeBytes.HasValue || s.SizeBytes <= 0)
                    continue;
                valid.Add((s.DateUtc, s.SizeBytes.Value));
            }
        }
        if (valid.Count < 2)
            return null;

        valid.Sort((a, b) => a.Date.CompareTo(b.Date));

        var first = valid[0].Date;
        var last = valid[valid.Count - 1].Date;
        var spanDays = Math.Max(1, (last - first).TotalDays);
        var minSize = valid.Min(p => p.Size);
        var maxSize = valid.Max(p => p.Size);
        if (maxSize <= minSize)
            maxSize = minSize + 1;

        var points = valid.Select(p => new SizeChartPoint(
            (p.Date - first).TotalDays / spanDays,
            (p.Size - minSize) / (double)(maxSize - minSize),
            p.Date.ToString("dd.MM"),
            p.Size)).ToList();

        return new SizeChartData
        {
            Points = points,
            YMaxLabel = DiskForecastService.FormatBytes(maxSize),
            YMinLabel = DiskForecastService.FormatBytes(minSize),
            XMinLabel = first.ToString("dd.MM.yyyy"),
            XMaxLabel = last.ToString("dd.MM.yyyy")
        };
    }
}
