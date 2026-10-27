using System;
using System.Collections.Generic;
using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты «Мониторинг лицензий» (п.5, выпуск 1.0.0.87): разбор «rac session list
/// --licenses» (табличный и блочный форматы 8.3/8.5), разбор «ring license list»/
/// «licensetool», расчёт сводки свободных лицензий и логика порога с подавлением повторов.
/// </summary>
public sealed class LicenseTests
{
    // ===================== rac session list --licenses (табличный вывод 8.3.x) =====================

    [Fact]
    public void ToSessions_ParsesLicenseColumns_FromTabularHeader()
    {
        // rac 8.3.x «session list --licenses»: к стандартным 24 колонкам добавляются
        // lic-type, max-licenses, licenses (позиции распознаются по строке заголовка).
        const string header =
            "cluster\tsession\tinfobase\tuser-name\thost\tapp-id\tstarted-at\tlast-active-at\tblocked-by-ls\tblocked-by-deadlock\tdb-proc-duration\tduration-all\tduration-current\tduration-dbms\tduration-cpu\tduration-wait\tmemory\tbytes\tposition\tread\twrite\tconnection\thibernate\tstate\tlic-type\tmax-licenses\tlicenses";
        const string row =
            "8b2f6f5e-6e3c-4c5a-8a9b-1c2d3e4f5a6b\t11111111-2222-3333-4444-555555555555\t99999999-8888-7777-6666-555555555555\tИванов Иван\tWORKSTATION-01\t1CV8\t2026-09-28T09:30:00\t2026-09-28T09:45:12\t0\t1\t0\t1250\t350\t900\t200\t150\t104857600\t12345678\t0\t1024\t2048\tbbbbbbbb-0000-1111-2222-333344445555\t0\tactive\tserver\t32\t2";
        var output = header + "\n" + row + "\n";

        var session = Assert.Single(RacOutputParser.ToSessions(output));

        Assert.Equal("Иванов Иван", session.User);
        Assert.Equal("active", session.State);
        Assert.Equal("server", session.LicType);
        Assert.Equal(32, session.MaxLicenses);
        Assert.Equal(2, session.Licenses);
    }

    [Fact]
    public void ToSessions_WithoutLicenseHeader_KeepsDefaults()
    {
        // Обычный «session list» без колонок лицензий: поля лицензий — по умолчанию.
        const string output =
            "cluster\tsession\tinfobase\tuser-name\thost\tapp-id\n" +
            "111-222\t4444aaaa-0000-0000-0000-000000000001\t\tПетров\tclient1\t1CV8\n";

        var session = Assert.Single(RacOutputParser.ToSessions(output));

        Assert.Equal("Петров", session.User);
        Assert.Equal(string.Empty, session.LicType);
        Assert.Equal(0, session.MaxLicenses);
        Assert.Equal(0, session.Licenses);
    }

    [Fact]
    public void ToSessions_ParsesLicenseFields_FromKeyValueBlocks()
    {
        // Новые версии rac (8.5.x): «session list --licenses» блоками «ключ : значение».
        const string output =
            "session : 4444aaaa-0000-0000-0000-000000000001\n" +
            "user-name : \"Иванов\"\n" +
            "host : client1\n" +
            "app-id : 1CV8\n" +
            "lic-type : client\n" +
            "max-licenses : 20\n" +
            "licenses : 1\n" +
            "session : 4444aaaa-0000-0000-0000-000000000002\n" +
            "user-name : \"Петров\"\n" +
            "lic-type : web\n" +
            "max-licenses : 10\n" +
            "licenses : 2\n";

        var sessions = RacOutputParser.ToSessions(output);

        Assert.Equal(2, sessions.Count);
        var first = sessions[0];
        Assert.Equal("Иванов", first.User);
        Assert.Equal("client", first.LicType);
        Assert.Equal(20, first.MaxLicenses);
        Assert.Equal(1, first.Licenses);

        var second = sessions[1];
        Assert.Equal("Петров", second.User);
        Assert.Equal("web", second.LicType);
        Assert.Equal(2, second.Licenses);
    }

    [Fact]
    public void ToSessions_MultipleSessionsWithoutLicType_StillParsesUsers()
    {
        // Вывод без залицензированных сеансов: lic-type пуст у всех — парсер не падает.
        const string output =
            "session : 4444aaaa-0000-0000-0000-000000000001\n" +
            "user-name : \"Без лицензии\"\n" +
            "host : client2\n" +
            "session : 4444aaaa-0000-0000-0000-000000000002\n" +
            "user-name : \"Сидоров\"\n";

        var sessions = RacOutputParser.ToSessions(output);

        Assert.Equal(2, sessions.Count);
        Assert.All(sessions, s => Assert.Equal(string.Empty, s.LicType));
    }

    // ===================== ring license list / licensetool =====================

    [Fact]
    public void RingLicenseParser_ParsesTableWithHeader()
    {
        const string output =
            "Серийный номер | Тип           | Версия  | Модуль       | До\n" +
            "1C123456-7890-1234-5678-901234567890 | клиентская | 8.3.27 | 1C:Enterprise | 31.12.2030\n";

        var licenses = RingLicenseParser.Parse(output);

        var license = Assert.Single(licenses);
        Assert.Equal("1C123456-7890-1234-5678-901234567890", license.SerialNumber);
        Assert.Equal("клиентская", license.Type);
        Assert.Equal("8.3.27", license.Version);
        Assert.Equal(2030, license.ExpiryDate?.Year);
    }

    [Fact]
    public void RingLicenseParser_ParsesFreeFormWithSerialOnly()
    {
        const string output =
            "Issued software licenses:\n" +
            "  Serial number: 8a9b8c7d-1234-5678-9012-345678901234  Type: server  Version: 8.3.27\n" +
            "  Serial number: 8a9b8c7d-1234-5678-9012-345678901235\n";

        var licenses = RingLicenseParser.Parse(output);

        Assert.Equal(2, licenses.Count);
        Assert.Equal("8a9b8c7d-1234-5678-9012-345678901234", licenses[0].SerialNumber);
        Assert.Equal("server", licenses[0].Type);
        Assert.Equal("8.3.27", licenses[0].Version);
    }

    [Fact]
    public void RingLicenseParser_EmptyOrGarbage_ReturnsEmpty()
    {
        Assert.Empty(RingLicenseParser.Parse(string.Empty));
        Assert.Empty(RingLicenseParser.Parse("  \n  \n"));
        Assert.Empty(RingLicenseParser.Parse("Просто текст без серийных номеров"));
    }

    [Fact]
    public void RingLicenseParser_TabularUtf8WithSpaces_NoThrow()
    {
        // Выравнивание пробелами (как в консоли Linux): 2+ пробела — разделитель колонок.
        const string output =
            "Serial number                            Type      Platform version\n" +
            "ABCDEF12-3456-7890-ABCD-EF1234567890      КЛИЕНТ    8.3.27";

        var licenses = RingLicenseParser.Parse(output);

        Assert.Single(licenses);
        Assert.Equal("ABCDEF12-3456-7890-ABCD-EF1234567890", licenses[0].SerialNumber);
    }

    // ===================== LicenseCalculator: сводка =====================

    [Fact]
    public void ComputeSnapshot_SumsUsedByType_AndInstalledTotal()
    {
        var sessions = new List<RacSessionInfo>
        {
            new() { User = "Иванов", LicType = "client", Licenses = 1 },
            new() { User = "Петров", LicType = "client", Licenses = 1 },
            new() { User = "Сидоров", LicType = "web", Licenses = 0 } // без поля Licenses → 1
        };
        var installed = new List<ProgramLicense>
        {
            new() { Type = "client", LicenseCount = 10 },
            new() { Type = "web", LicenseCount = 5 }
        };

        var snapshot = LicenseCalculator.ComputeSnapshot(sessions, installed);

        Assert.Equal(3, snapshot.TotalUsed);
        Assert.Equal(15, snapshot.TotalInstalled);
        Assert.Equal(12, snapshot.TotalFree);
        Assert.Equal(2, snapshot.Entries.Count);
    }

    [Fact]
    public void ComputeSnapshot_WithoutInstalledLicenses_HasNoFreeTotal()
    {
        var sessions = new List<RacSessionInfo> { new() { LicType = "client" } };

        var snapshot = LicenseCalculator.ComputeSnapshot(sessions, Array.Empty<ProgramLicense>());

        Assert.Equal(1, snapshot.TotalUsed);
        Assert.Equal(0, snapshot.TotalInstalled);
        Assert.Null(snapshot.TotalFree);
    }

    [Fact]
    public void ComputeSnapshot_LicensedSessionWithoutType_IsIgnored()
    {
        var sessions = new List<RacSessionInfo> { new() { User = "Иванов", LicType = "" } };

        var snapshot = LicenseCalculator.ComputeSnapshot(sessions, Array.Empty<ProgramLicense>());

        Assert.Equal(0, snapshot.TotalUsed);
    }

    // ===================== LicenseCalculator: порог =====================

    [Fact]
    public void CheckThreshold_SendsAlert_WhenFreeBelowThreshold()
    {
        var lastSent = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        var now = new DateTime(2026, 10, 27, 12, 0, 0);

        var alerts = LicenseCalculator.CheckThreshold(
            "srv1", totalLicenses: 10, usedLicenses: 9, freeLicenses: 1,
            threshold: 3, now, lastSent, repeatHours: 24);

        var alert = Assert.Single(alerts);
        Assert.Contains("1 из 10", alert);
        Assert.Contains("свободно меньше 3", alert);
    }

    [Fact]
    public void CheckThreshold_NoAlert_WhenThresholdDisabled()
    {
        var alerts = LicenseCalculator.CheckThreshold(
            "srv1", 10, 9, 1, threshold: 0, DateTime.Now,
            new Dictionary<string, DateTime>(StringComparer.Ordinal), repeatHours: 24);

        Assert.Empty(alerts);
    }

    [Fact]
    public void CheckThreshold_NoAlert_WhenFreeNotBelowThreshold()
    {
        var alerts = LicenseCalculator.CheckThreshold(
            "srv1", 10, 2, 8, threshold: 3, DateTime.Now,
            new Dictionary<string, DateTime>(StringComparer.Ordinal), repeatHours: 24);

        Assert.Empty(alerts);
    }

    [Fact]
    public void CheckThreshold_NoAlert_WhenInstalledUnknown()
    {
        var alerts = LicenseCalculator.CheckThreshold(
            "srv1", 0, 2, null, threshold: 3, DateTime.Now,
            new Dictionary<string, DateTime>(StringComparer.Ordinal), repeatHours: 24);

        Assert.Empty(alerts);
    }

    [Fact]
    public void CheckThreshold_SuppressesRepeatWithinRepeatHours()
    {
        var lastSent = new Dictionary<string, DateTime>(StringComparer.Ordinal)
        {
            [LicenseCalculator.LicenseAlertKey("srv1")] = new DateTime(2026, 10, 27, 11, 0, 0)
        };
        var now = new DateTime(2026, 10, 27, 12, 0, 0);

        var alerts = LicenseCalculator.CheckThreshold(
            "srv1", 10, 9, 1, threshold: 3, now, lastSent, repeatHours: 3);

        Assert.Empty(alerts);
    }

    [Fact]
    public void CheckThreshold_AllowsRepeatAfterRepeatHours()
    {
        var lastSent = new Dictionary<string, DateTime>(StringComparer.Ordinal)
        {
            [LicenseCalculator.LicenseAlertKey("srv1")] = new DateTime(2026, 10, 27, 6, 0, 0)
        };
        var now = new DateTime(2026, 10, 27, 12, 0, 0);

        var alerts = LicenseCalculator.CheckThreshold(
            "srv1", 10, 9, 1, threshold: 3, now, lastSent, repeatHours: 3);

        Assert.NotEmpty(alerts);
    }

    // ===================== LicenseManager (оповещение) =====================

    [Fact]
    public void LicenseManager_CheckAndNotify_SendsOnce_AndSuppressesSecond()
    {
        var notifications = new RecordingNotificationService();
        var manager = new LicenseManager(notifications);

        var snapshot = LicenseCalculator.ComputeSnapshot(
            new List<RacSessionInfo> { new() { LicType = "client" } },
            new List<ProgramLicense> { new() { Type = "client", LicenseCount = 2 } });

        var now = new DateTime(2026, 10, 27, 12, 0, 0);
        var sent = manager.CheckAndNotify("srv1", snapshot, threshold: 2, now, repeatHours: 24);
        var sentAgain = manager.CheckAndNotify("srv1", snapshot, threshold: 2, now.AddMinutes(30), repeatHours: 24);

        Assert.True(sent);
        Assert.False(sentAgain);
        Assert.Single(notifications.Messages);
        Assert.Equal(NotificationEvent.Monitoring, notifications.Messages[0].Event);
    }

    [Fact]
    public void LicenseManager_CheckAndNotify_DisabledThreshold_DoesNotSend()
    {
        var notifications = new RecordingNotificationService();
        var manager = new LicenseManager(notifications);

        var snapshot = LicenseCalculator.ComputeSnapshot(
            new List<RacSessionInfo> { new() { LicType = "client" } },
            new List<ProgramLicense> { new() { Type = "client", LicenseCount = 2 } });

        var sent = manager.CheckAndNotify("srv1", snapshot, threshold: 0, DateTime.Now, repeatHours: 24);

        Assert.False(sent);
        Assert.Empty(notifications.Messages);
    }

    [Fact]
    public void LicenseManager_CheckAndNotify_UnknownFree_NeverSpams()
    {
        var notifications = new RecordingNotificationService();
        var manager = new LicenseManager(notifications);

        // Установленные лицензии недоступны (пусто) — TotalFree null; оповещение не уходит.
        var snapshot = LicenseCalculator.ComputeSnapshot(
            new List<RacSessionInfo> { new() { LicType = "client" } },
            Array.Empty<ProgramLicense>());

        var sent = manager.CheckAndNotify("srv1", snapshot, threshold: 2, DateTime.Now, repeatHours: 24);

        Assert.False(sent);
        Assert.Empty(notifications.Messages);
    }

    /// <summary>Записывающий INotificationService для тестов.</summary>
    private sealed class RecordingNotificationService : INotificationService
    {
        public List<NotificationMessage> Messages { get; } = new();

        public void Show(string title, string message) =>
            Messages.Add(new NotificationMessage { Title = title, Message = message });

        public void Show(string title, string message, NotificationKind kind, NotificationEvent evt)
            => Messages.Add(new NotificationMessage
            {
                Title = title,
                Message = message,
                Kind = kind,
                Event = evt
            });
    }
}
