using System;
using System.Collections.Generic;
using System.Linq;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>Строка сводки по одному типу лицензии (п.5 «Мониторинг лицензий»).</summary>
public sealed class LicenseSummaryEntry
{
    /// <summary>Тип лицензии (значение «lic-type» rac / тип из ring).</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Установлено всего (из «ring license list»; 0 — тип не сопоставлен с установленной лицензией).</summary>
    public int Total { get; set; }

    /// <summary>Занято сеансами (число сеансов с непустым lic-type, с учётом поля «licenses»).</summary>
    public int Used { get; set; }

    /// <summary>Свободно (Total - Used; не меньше 0).</summary>
    public int Free => Math.Max(0, Total - Used);
}

/// <summary>
/// Снимок использования лицензий кластера 1С (п.5, выпуск 1.0.0.87): синтез данных
/// rac («session list --licenses») и установленных программных лицензий («ring license
/// list» / «licensetool»). Чистая логика — без I/O и UI (тестируется отдельно).
/// </summary>
public sealed class LicenseSnapshot
{
    /// <summary>Сводка в разрезе типов лицензий (ключ — тип, порядок стабильный по первому появлению).</summary>
    public IReadOnlyList<LicenseSummaryEntry> Entries { get; init; } = Array.Empty<LicenseSummaryEntry>();

    /// <summary>Занято лицензий по всем типам (сумма полей «licenses» сеансов; сеанс без поля считает занятие 1).</summary>
    public int TotalUsed { get; init; }

    /// <summary>Установлено лицензий по всем типам (сумма «LicenseCount» установленных; строка без числа = 1).</summary>
    public int TotalInstalled { get; init; }

    /// <summary>
    /// Свободно лицензий по всем типам; null — список установленных лицензий недоступен
    /// (утилита ring/licensetool не найдена или вывела пустой список) — проверить порог
    /// невозможно, оповещение не отправляется.
    /// </summary>
    public int? TotalFree
    {
        get
        {
            if (TotalInstalled == 0 && Entries.All(e => e.Total <= 0))
                return null;
            return Math.Max(0, TotalInstalled - TotalUsed);
        }
    }
}

/// <summary>
/// Расчёт сводки лицензий и проверка порога «свободно меньше N» (п.5, 1.0.0.87).
/// Чистые функции — без I/O и UI.
/// </summary>
public static class LicenseCalculator
{
    /// <summary>
    /// Собирает снимок: по типам <see cref="RacSessionInfo.LicType"/> — занято,
    /// по установленным <see cref="ProgramLicense"/> — всего. Установленные лицензии
    /// группируются по своей строке «тип»; сопоставление с lic-type rac не требуется
    /// для суммарной сводки (Total/Used/Free).
    /// </summary>
    public static LicenseSnapshot ComputeSnapshot(
        IReadOnlyList<RacSessionInfo> licensedSessions,
        IReadOnlyList<ProgramLicense> installed)
    {
        var usedByType = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();

        foreach (var session in licensedSessions ?? Array.Empty<RacSessionInfo>())
        {
            // Сеанс занимает лицензию, когда rac вернул тип (lic-type непустой);
            // число занятых — поле «licenses», иначе 1 (один сеанс = одна лицензия).
            if (string.IsNullOrWhiteSpace(session.LicType))
                continue;

            var key = session.LicType.Trim();
            if (!usedByType.TryAdd(key, 0))
                order.Add(key);
            usedByType[key] += Math.Max(1, session.Licenses);
        }

        var installedByType = new Dictionary<string, (int Count, int RawEntries)>(StringComparer.OrdinalIgnoreCase);
        foreach (var license in installed ?? Array.Empty<ProgramLicense>())
        {
            var key = NormalizeInstalledType(license.Type);
            if (!installedByType.TryGetValue(key, out var existing))
                installedByType[key] = (Math.Max(1, license.LicenseCount), 1);
            else
                installedByType[key] = (existing.Count + Math.Max(1, license.LicenseCount), existing.RawEntries + 1);
        }

        var entries = new List<LicenseSummaryEntry>();
        foreach (var key in order)
        {
            var used = usedByType[key];
            installedByType.TryGetValue(key, out var installedTotal);
            entries.Add(new LicenseSummaryEntry
            {
                Type = key,
                Total = installedTotal.RawEntries > 0 ? installedTotal.Count : 0,
                Used = used
            });
        }

        // Установленные типы, не встреченные в сеансах, также попадают в сводку
        // (Total>0, Used=0) — пользователь видит свободные лицензии целиком.
        foreach (var pair in installedByType)
        {
            if (entries.All(e => !string.Equals(e.Type, pair.Key, StringComparison.OrdinalIgnoreCase)))
            {
                entries.Add(new LicenseSummaryEntry
                {
                    Type = pair.Key,
                    Total = pair.Value.Count,
                    Used = 0
                });
            }
        }

        var totalUsed = usedByType.Values.Sum();
        var totalInstalled = installedByType.Values.Sum(v => v.Count);

        return new LicenseSnapshot
        {
            Entries = entries,
            TotalUsed = totalUsed,
            TotalInstalled = totalInstalled
        };
    }

    /// <summary>Нормализация типа установленной лицензии для сопоставления со сводкой.</summary>
    private static string NormalizeInstalledType(string type)
    {
        var t = (type ?? string.Empty).Trim();
        return t.Length == 0 ? "—" : t;
    }

    /// <summary>
    /// Проверяет порог «свободно меньше N» по сумме всех типов. Возвращает тексты
    /// оповещений, которые пора отправить (с учётом подавления повторов в
    /// <paramref name="lastSent"/>: не чаще раза в <paramref name="repeatHours"/> часов).
    /// <paramref name="threshold"/> ≤ 0 — порог отключён (не проверяем).
    /// <paramref name="freeLicenses"/> null — установленные лицензии недоступны (проверить нельзя,
    /// оповещение НЕ шлём, чтобы не спамить при отсутствии ring/licensetool).
    /// </summary>
    public static IReadOnlyList<string> CheckThreshold(
        string serverLabel,
        int totalLicenses,
        int usedLicenses,
        int? freeLicenses,
        int threshold,
        DateTime now,
        IReadOnlyDictionary<string, DateTime> lastSent,
        int repeatHours)
    {
        var alerts = new List<string>();
        if (threshold <= 0 || freeLicenses is not int free || free >= threshold)
            return alerts;

        var key = LicenseAlertKey(serverLabel);
        var repeat = TimeSpan.FromHours(Math.Max(1, repeatHours));
        if (lastSent.TryGetValue(key, out var last) && now - last < repeat)
            return alerts;

        alerts.Add($"{serverLabel}: свободных программных лицензий {free} из {totalLicenses} " +
                   $"(занято {usedLicenses}; порог «свободно меньше {threshold}»).");
        return alerts;
    }

    /// <summary>Ключ подавления повторов оповещения о лицензиях сервера.</summary>
    public static string LicenseAlertKey(string serverLabel) => "license:" + serverLabel;
}

/// <summary>
/// Оповещения о нехватке лицензий (п.5, выпуск 1.0.0.87): проверка порога «свободно
/// меньше N» с защитой от спама (повторное оповещение о том же сервере — не чаще раза
/// в N часов). Оповещение уходит через <see cref="INotificationService"/> событием
/// <see cref="NotificationEvent.Monitoring"/> — в существующие каналы (системное
/// уведомление, Telegram, email) при включённом <c>NotifyOnMonitoring</c>.
/// </summary>
public sealed class LicenseManager
{
    private readonly INotificationService _notifications;
    private readonly IAppLogger? _logger;
    private readonly Dictionary<string, DateTime> _lastSentByKey = new(StringComparer.Ordinal);

    public LicenseManager(INotificationService notifications, IAppLogger? logger = null)
    {
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _logger = logger;
    }

    /// <summary>Время последней подавленной отправки по ключу (internal — для тестов).</summary>
    internal IReadOnlyDictionary<string, DateTime> LastSentByKey => _lastSentByKey;

    /// <summary>
    /// Проверяет порог по снимку и отправляет оповещение (если пора). <paramref name="now"/>
    /// — момент проверки (injектируется в тестах). Возвращает true, если оповещение ушло.
    /// </summary>
    public bool CheckAndNotify(
        string serverLabel,
        LicenseSnapshot snapshot,
        int threshold,
        DateTime now,
        int repeatHours = 24)
    {
        if (snapshot is null)
            return false;

        var alerts = LicenseCalculator.CheckThreshold(
            serverLabel, snapshot.TotalInstalled, snapshot.TotalUsed,
            snapshot.TotalFree, threshold, now, _lastSentByKey, repeatHours);
        if (alerts.Count == 0)
            return false;

        _lastSentByKey[LicenseCalculator.LicenseAlertKey(serverLabel)] = now;
        foreach (var text in alerts)
        {
            _logger?.Info($"ЛИЦЕНЗИИ: {text}");
            _notifications.Show("Workspace 1C — лицензии", text,
                NotificationKind.Warning, NotificationEvent.Monitoring);
        }
        return true;
    }

    /// <summary>Полный сброс подавления повторов (для тестов).</summary>
    internal void Reset()
    {
        _lastSentByKey.Clear();
    }
}
