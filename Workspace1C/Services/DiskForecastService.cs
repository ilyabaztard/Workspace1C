using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Результат линейной аппроксимации (метод наименьших квадратов) по точкам
/// (X — дни от начала, Y — байты).
/// </summary>
public sealed record LinearFit(double Slope, double Intercept, double R2, bool Valid);

/// <summary>
/// Прогноз «диск под копиями закончится примерно через N дней».
/// </summary>
public sealed record DiskForecastResult(
    double DaysUntilFull, int DaysMin, int DaysMax, DateTime? DateWhenFull,
    double GrowthPerDayBytes, double Confidence, bool Valid)
{
    public static readonly DiskForecastResult None = new(0, 0, 0, null, 0, 0, false);
}

/// <summary>
/// Прогноз роста размеров баз и заполнения диска копиями (п.7 «История размеров баз и
/// прогноз диска», выпуск 1.0.0.89): линейная экстраполяция по последним N ≥ 2 дневным
/// снимкам (метод наименьших квадратов). Чистые функции без I/O и UI — покрыты
/// юнит-тестами (<see cref="Workspace1C.Tests.DiskForecastTests"/>).
///
/// Правила:
/// - нулевые/битые замеры (null, ≤ 0 по растущей метрике) игнорируются;
/// - рост ≤ 0 — диск не заполняется, прогноза нет (Valid = false);
/// - «примерно N дней» — диапазон: [N·(1−spread), N·(1+spread)] по разбросу точек (R²).
/// </summary>
public static class DiskForecastService
{
    /// <summary>
    /// Линейная аппроксимация точек (X — день от начала, Y — метрика в байтах).
    /// Менее 2 точек — Valid = false. Slope — прирост в байтах/день.
    /// </summary>
    public static LinearFit FitLinear(IReadOnlyList<(double X, double Y)> points)
    {
        if (points is null || points.Count < 2)
            return new LinearFit(0, 0, 0, false);

        var n = points.Count;
        double sx = 0, sy = 0, sxy = 0, sxx = 0;
        foreach (var p in points)
        {
            sx += p.X;
            sy += p.Y;
            sxy += p.X * p.Y;
            sxx += p.X * p.X;
        }
        var denom = n * sxx - sx * sx;
        if (Math.Abs(denom) < 1e-12)
            return new LinearFit(0, 0, 0, false);

        var slope = (n * sxy - sx * sy) / denom;
        var intercept = (sy - slope * sx) / n;

        // Коэффициент детерминации R²: насколько линия объясняет разброс точек.
        double r2;
        var meanY = sy / n;
        double ssTot = 0, ssRes = 0;
        foreach (var p in points)
        {
            ssTot += (p.Y - meanY) * (p.Y - meanY);
            var predicted = slope * p.X + intercept;
            ssRes += (p.Y - predicted) * (p.Y - predicted);
        }
        r2 = ssTot < 1e-9
            ? 1.0                                    // все точки на одной линии (или Y постоянен)
            : Math.Max(0, Math.Min(1, 1 - ssRes / ssTot));

        return new LinearFit(slope, intercept, r2, true);
    }

    /// <summary>
    /// Прирост метрики в байтах/день по последним снимкам (в отсортированном по дате
    /// списке последние N ≥ 2 непустых значений; <paramref name="selector"/> выбирает
    /// метрику: <see cref="SizeSnapshotEntry.SizeBytes"/> или <see cref="SizeSnapshotEntry.CopiesBytes"/>).
    /// Нет данных или рост ≤ 0 — null.
    /// </summary>
    public static double? EstimateGrowthRate(
        IReadOnlyList<SizeSnapshotEntry> snapshots,
        Func<SizeSnapshotEntry, long?> selector)
    {
        var points = BuildPoints(snapshots, selector);
        if (points.Count < 2)
            return null;
        var fit = FitLinear(points);
        if (!fit.Valid || fit.Slope <= 0)
            return null;
        return fit.Slope;
    }

    /// <summary>
    /// Полный прогноз «диск под копиями закончится»: скорость роста
    /// <paramref name="selector"/>-метрики по снимкам против свободного места
    /// <paramref name="freeBytes"/> (обычно <see cref="SizeSnapshotEntry.CopiesDiskFreeBytes"/>
    /// последнего снимка). Возвращает дни до заполнения с диапазоном и датой,
    /// <see cref="DiskForecastResult.None"/> при недостатке данных/отрицательном росте.
    /// </summary>
    public static DiskForecastResult ForecastDaysUntilFull(
        IReadOnlyList<SizeSnapshotEntry> snapshots,
        long? freeBytes,
        Func<SizeSnapshotEntry, long?> selector,
        DateTime nowUtc)
    {
        if (freeBytes is null or <= 0)
            return DiskForecastResult.None;

        var points = BuildPoints(snapshots, selector);
        if (points.Count < 2)
            return DiskForecastResult.None;

        var fit = FitLinear(points);
        if (!fit.Valid || fit.Slope <= 1e-9)
            return DiskForecastResult.None;

        var days = freeBytes.Value / fit.Slope;
        if (days <= 0 || double.IsInfinity(days) || double.IsNaN(days))
            return DiskForecastResult.None;

        // «Примерно N дней»: чем хуже R², тем шире диапазон (до ±50% при R²=0).
        var spread = (1 - fit.R2) * 0.5;
        var daysMin = Math.Max(1, (int)Math.Floor(days * (1 - spread)));
        var daysMax = Math.Max(daysMin + 1, (int)Math.Ceiling(days * (1 + spread)));

        var dateWhenFull = nowUtc.Date.AddDays(Math.Ceiling(days));

        return new DiskForecastResult(
            days, daysMin, daysMax, dateWhenFull,
            fit.Slope, fit.R2, true);
    }

    /// <summary>
    /// Строит точки регрессии: X — число дней от даты самого раннего валидного снимка,
    /// Y — значение метрики. Валидный снимок — не null и строго больше 0 (для растущей
    /// метрики); снимки с одинаковым днём сливаются (берётся последний в списке).
    /// </summary>
    public static List<(double X, double Y)> BuildPoints(
        IReadOnlyList<SizeSnapshotEntry> snapshots,
        Func<SizeSnapshotEntry, long?> selector)
    {
        var result = new List<(double, double)>();
        if (snapshots is null || snapshots.Count == 0)
            return result;

        var valid = new List<SizeSnapshotEntry>();
        foreach (var s in snapshots)
        {
            if (s is null) continue;
            var v = selector(s);
            if (v.HasValue && v.Value > 0)
                valid.Add(s);
        }
        if (valid.Count < 2)
            return result;

        // Слияние одинаковых дней: оставляем последний по списку снимок дня.
        var byDay = new Dictionary<DateTime, SizeSnapshotEntry>();
        foreach (var s in valid)
            byDay[s.DateUtc] = s;

        var days = byDay.Keys.OrderBy(d => d).ToList();
        if (days.Count < 2)
            return result;

        var start = days[0];
        foreach (var day in days)
        {
            var x = (day - start).TotalDays;
            var y = (double)selector(byDay[day])!.Value;
            result.Add((x, y));
        }
        return result;
    }

    /// <summary>
    /// Проверка порога «диск закончится через N дней» (п.7, 1.0.0.89): если прогноз
    /// валиден и дней до заполнения ≤ порог — возвращает текст оповещения, который пора
    /// отправить (с учётом подавления повторов в <paramref name="lastSent"/>: не чаще раза
    /// в <paramref name="repeatDays"/> дней). Порог ≤ 0 — отключено. Пустой список —
    /// отправлять нечего.
    /// </summary>
    public static IReadOnlyList<string> CheckThreshold(
        DiskForecastResult forecast,
        string driveLabel,
        int thresholdDays,
        DateTime now,
        IReadOnlyDictionary<string, DateTime> lastSent,
        int repeatDays)
    {
        var alerts = new List<string>();
        if (thresholdDays <= 0 || forecast is null || !forecast.Valid)
            return alerts;
        if (forecast.DaysUntilFull > thresholdDays)
            return alerts;

        var key = DiskForecastAlertKey(driveLabel);
        var repeat = TimeSpan.FromDays(Math.Max(1, repeatDays));
        if (lastSent.TryGetValue(key, out var last) && now - last < repeat)
            return alerts;

        var days = (int)Math.Ceiling(forecast.DaysUntilFull);
        var date = forecast.DateWhenFull?.ToString("dd.MM.yyyy") ?? "?";
        var growth = FormatBytes(forecast.GrowthPerDayBytes);
        var text = string.Format(
            Workspace1C.Localization.LocalizationManager.T("Alerts.DiskForecast"),
            driveLabel, days, forecast.DaysMin, forecast.DaysMax, date, growth);
        alerts.Add(text);
        return alerts;
    }

    /// <summary>Ключ подавления повторов оповещения о заполнении диска копиями.</summary>
    public static string DiskForecastAlertKey(string driveLabel) => "diskforecast:" + driveLabel;

    /// <summary>Форматирует размер в байтах: «12,4 ГБ / день» (запятая — единый формат UI).</summary>
    public static string FormatBytes(double bytes)
    {
        const long gb = 1024L * 1024 * 1024;
        const long mb = 1024L * 1024;
        if (bytes >= gb)
            return $"{bytes / gb:0.#}".Replace('.', ',') + " ГБ";
        if (bytes >= mb)
            return $"{bytes / mb:0.#}".Replace('.', ',') + " МБ";
        if (bytes >= 1024)
            return $"{bytes / 1024:0.#}".Replace('.', ',') + " КБ";
        return $"{bytes:0} Б";
    }
}
