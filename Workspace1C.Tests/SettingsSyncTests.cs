using System;
using System.IO;
using System.Linq;
using System.Text;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Services.Sync;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты п.9 «Синхронизация своих настроек между компьютерами» (1.0.0.91):
/// план операций last-write-wins (<see cref="SettingsSyncPlan.Build"/>), имена
/// конфликтных резервов, безопасность путей (zip-slip), zip-пакет round-trip,
/// чтение/запись настроек (<see cref="SettingsSyncStore"/>), валидация, исполнение
/// синхронизации по сетевой папке (выгрузка/скачивание/конфликт-бэкапы, отсутствие
/// «пинг-понга»), шифрование пароля и маскировка (пароль не попадает в журнал).
/// </summary>
[Collection("LocalDataStoreState")]
public class SettingsSyncTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), "w1c_sync_tests_" + Guid.NewGuid().ToString("N"));

    public SettingsSyncTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Временная папка теста — чистка без фейла при блокировке файлов.
        }
        FeatureDataStore.DirectoryOverride = null;
    }

    private string Dir(string name)
    {
        var path = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static DateTime Utc(int day, int hour) =>
        new DateTime(2026, 1, day, hour, 0, 0, DateTimeKind.Utc);

    // ---------- План операций: last-write-wins ----------

    [Fact]
    public void BuildPlan_LocalOnly_Upload()
    {
        var plan = SettingsSyncPlan.Build(
            new[] { new SyncFileState("settings.json", Utc(1, 10), 100) },
            Array.Empty<SyncFileState>());

        var entry = Assert.Single(plan);
        Assert.Equal(SyncOperation.Upload, entry.Operation);
        Assert.False(entry.NeedsConflictBackup);
    }

    [Fact]
    public void BuildPlan_RemoteOnly_Download()
    {
        var plan = SettingsSyncPlan.Build(
            Array.Empty<SyncFileState>(),
            new[] { new SyncFileState("settings.json", Utc(1, 10), 100) });

        var entry = Assert.Single(plan);
        Assert.Equal(SyncOperation.Download, entry.Operation);
        Assert.False(entry.NeedsConflictBackup);
    }

    [Fact]
    public void BuildPlan_LocalNewer_Upload()
    {
        var plan = SettingsSyncPlan.Build(
            new[] { new SyncFileState("settings.json", Utc(2, 12), 100) },
            new[] { new SyncFileState("settings.json", Utc(1, 8), 100) });

        var entry = Assert.Single(plan);
        Assert.Equal(SyncOperation.Upload, entry.Operation);
        Assert.True(entry.NeedsConflictBackup);
    }

    [Fact]
    public void BuildPlan_RemoteNewer_Download()
    {
        var plan = SettingsSyncPlan.Build(
            new[] { new SyncFileState("settings.json", Utc(1, 8), 100) },
            new[] { new SyncFileState("settings.json", Utc(2, 12), 200) });

        var entry = Assert.Single(plan);
        Assert.Equal(SyncOperation.Download, entry.Operation);
        Assert.True(entry.NeedsConflictBackup);
    }

    [Fact]
    public void BuildPlan_EqualTimestamps_Skip()
    {
        var plan = SettingsSyncPlan.Build(
            new[] { new SyncFileState("settings.json", Utc(1, 10), 100) },
            new[] { new SyncFileState("settings.json", Utc(1, 10), 100) });

        var entry = Assert.Single(plan);
        Assert.Equal(SyncOperation.Skip, entry.Operation);
    }

    [Fact]
    public void BuildPlan_Tolerance_SmallDelta_Skip()
    {
        var a = new SyncFileState("settings.json", Utc(1, 10).AddSeconds(1), 100);
        var b = new SyncFileState("settings.json", Utc(1, 10).AddSeconds(-1), 100);

        Assert.Equal(SyncOperation.Skip, SettingsSyncPlan.Build(new[] { a }, new[] { b })[0].Operation);
    }

    [Fact]
    public void BuildPlan_RemoteExtraFile_DownloadOnlyForIt()
    {
        var plan = SettingsSyncPlan.Build(
            new[] { new SyncFileState("settings.json", Utc(1, 10), 100) },
            new[]
            {
                new SyncFileState("settings.json", Utc(1, 10), 100),
                new SyncFileState("groups.json", Utc(2, 9), 50)
            });

        Assert.Equal(2, plan.Count);
        Assert.Equal(SyncOperation.Skip, plan[0].Operation);
        Assert.Equal(SyncOperation.Download, plan[1].Operation);
        Assert.Equal("groups.json", plan[1].RelativePath);
    }

    // ---------- Конфликтные резервы ----------

    [Fact]
    public void ConflictBackupName_Format_HasDateAndSide()
    {
        var name = SettingsSyncPlan.ConflictBackupName("scripts/scenarios/Calc.1.script.json", "local", Utc(5, 9));
        Assert.Contains(".local.conflict-20260105-", name);
        Assert.StartsWith("scripts_scenarios_Calc.1.script.json", name);
        Assert.EndsWith(".local.conflict-" + Utc(5, 9).ToString("yyyyMMdd-HHmmss"), name);
    }

    // ---------- Безопасные пути (zip-slip) ----------

    [Theory]
    [InlineData("settings.json", true)]
    [InlineData("scripts/scenarios/Calc.1.script.json", true)]
    [InlineData("../outside.json", false)]
    [InlineData("/etc/passwd", false)]
    [InlineData("..//escape", false)]
    [InlineData("", false)]
    [InlineData("a/b/../c.json", false)]
    [InlineData("C:/windows/system32", false)]
    public void IsSafeRelativePath_DetectsEscape(string path, bool expected)
    {
        Assert.Equal(expected, SettingsSyncPackage.IsSafeRelativePath(path));
    }

    [Fact]
    public void ZipSlip_ExtractionDoesNotEscapeTargetDirectory()
    {
        var target = Dir("extract");
        var evil = Path.Combine(_tempDir, "evil_outside.txt");
        if (File.Exists(evil))
            File.Delete(evil);

        var archive = Path.Combine(_tempDir, "evil.zip");
        using (var zip = System.IO.Compression.ZipFile.Open(archive, System.IO.Compression.ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("../evil_outside.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("pwned");
        }

        SettingsSyncPackage.ExtractPackage(archive, target);

        Assert.False(File.Exists(evil), "zip-slip: файл не должен выйти за пределы каталога распаковки");
    }

    // ---------- Zip-пакет: round-trip ----------

    [Fact]
    public void Package_RoundTrip_PreservesFiles()
    {
        var sourceDir = Dir("package_src");
        var contentPath = Path.Combine(sourceDir, "settings.json");
        File.WriteAllText(contentPath, "{\"Theme\":\"dark\"}");
        File.SetLastWriteTimeUtc(contentPath, Utc(3, 14));

        var scenarioPath = Path.Combine(sourceDir, "scripts");
        Directory.CreateDirectory(scenarioPath);
        var scriptPath = Path.Combine(scenarioPath, "Calc.abc.script.json");
        File.WriteAllText(scriptPath, "{\"Name\":\"Calc\"}");

        var archive = Path.Combine(_tempDir, "snapshot.zip");
        Assert.True(SettingsSyncPackage.CreatePackage(archive, new[]
        {
            new System.Collections.Generic.KeyValuePair<string, string>("settings.json", contentPath),
            new System.Collections.Generic.KeyValuePair<string, string>("scripts/scenarios/Calc.abc.script.json", scriptPath)
        }));

        var target = Dir("package_dst");
        var count = SettingsSyncPackage.ExtractPackage(archive, target);
        Assert.Equal(2, count);
        Assert.Equal("{\"Theme\":\"dark\"}", File.ReadAllText(Path.Combine(target, "settings.json")));
        Assert.Equal("{\"Name\":\"Calc\"}", File.ReadAllText(Path.Combine(target, "scripts", "scenarios", "Calc.abc.script.json")));
        Assert.Equal(Utc(3, 14), File.GetLastWriteTimeUtc(Path.Combine(target, "settings.json")));
    }

    // ---------- Настройки: чтение/запись ----------

    [Fact]
    public void SettingsSyncStore_SaveLoad_RoundTrip()
    {
        FeatureDataStore.DirectoryOverride = Dir("profile");
        var s = new SettingsSyncSettings
        {
            Enabled = true,
            StorageKind = SettingsSyncStorageKind.WebDav,
            WebDavUrl = "https://webdav.example.com/sync",
            User = "me",
            PasswordEncrypted = "v2:abc",
            IntervalMinutes = 30,
            IncludePatterns = new System.Collections.Generic.List<string> { "settings.json" }
        };
        SettingsSyncStore.Save(s);

        var loaded = SettingsSyncStore.Load();
        Assert.True(loaded.Enabled);
        Assert.Equal(SettingsSyncStorageKind.WebDav, loaded.StorageKind);
        Assert.Equal("https://webdav.example.com/sync", loaded.WebDavUrl);
        Assert.Equal("me", loaded.User);
        Assert.Equal("v2:abc", loaded.PasswordEncrypted);
        Assert.Equal(30, loaded.IntervalMinutes);
        Assert.Equal(new[] { "settings.json" }, loaded.IncludePatterns);
    }

    [Fact]
    public void SettingsSync_DefaultPatterns_IncludeCoreFiles()
    {
        var patterns = SettingsSyncSettings.DefaultIncludePatterns;
        Assert.Contains("settings.json", patterns);
        Assert.Contains("infobases.json", patterns);
        Assert.Contains("groups.json", patterns);
        Assert.Contains("custom_actions.json", patterns);
        Assert.Contains("context_menu_layout.json", patterns);
        Assert.Contains("scripts/scenarios/*.script.json", patterns);
        Assert.Contains("ibases.v8i", patterns);
    }

    // ---------- Валидация ----------

    [Fact]
    public void Validate_WebDavWithoutUrl_ReturnsKey()
    {
        var s = new SettingsSyncSettings { StorageKind = SettingsSyncStorageKind.WebDav, WebDavUrl = "" };
        var errors = SettingsSyncService.Validate(s);
        Assert.Contains("SettingsSync.WebDavUrlRequired", errors);
    }

    [Fact]
    public void Validate_WebDavBadUrl_ReturnsKey()
    {
        var s = new SettingsSyncSettings { StorageKind = SettingsSyncStorageKind.WebDav, WebDavUrl = "not-a-url" };
        Assert.Contains("SettingsSync.WebDavUrlInvalid", SettingsSyncService.Validate(s));
    }

    [Fact]
    public void Validate_FolderWithoutPath_ReturnsKey()
    {
        var s = new SettingsSyncSettings { StorageKind = SettingsSyncStorageKind.NetworkFolder, FolderPath = "" };
        Assert.Contains("SettingsSync.FolderRequired", SettingsSyncService.Validate(s));
    }

    [Fact]
    public void Validate_FolderValid_NoErrors()
    {
        var s = new SettingsSyncSettings { StorageKind = SettingsSyncStorageKind.NetworkFolder, FolderPath = @"\\nas\share" };
        Assert.Empty(SettingsSyncService.Validate(s));
    }

    // ---------- Исполнение по сетевой папке ----------

    [Fact]
    public async Task Sync_FolderStorage_UploadsNewFile()
    {
        var local = Dir("sync_local");
        var remote = Dir("sync_remote");
        var settings = FolderSettings(remote, "settings.json");

        var localFile = Path.Combine(local, "settings.json");
        File.WriteAllText(localFile, "{\"Theme\":\"dark\"}");
        File.SetLastWriteTimeUtc(localFile, Utc(2, 10));

        var service = new SettingsSyncService(null, local);
        var log = new StringBuilder();
        var result = await service.SyncAsync(settings, line => log.AppendLine(line));

        Assert.True(result.Success);
        Assert.Contains("settings.json", result.ChangedFiles);
        Assert.Equal("{\"Theme\":\"dark\"}", File.ReadAllText(Path.Combine(remote, "settings.json")));
        Assert.Equal(File.GetLastWriteTimeUtc(localFile), File.GetLastWriteTimeUtc(Path.Combine(remote, "settings.json")));
        Assert.True(File.Exists(Path.Combine(remote, SettingsSyncService.RemoteMetaName)));
    }

    [Fact]
    public async Task Sync_FolderStorage_SecondRun_DoesNotPingPong()
    {
        var local = Dir("sync_local2");
        var remote = Dir("sync_remote2");
        var settings = FolderSettings(remote, "settings.json");

        var localFile = Path.Combine(local, "settings.json");
        File.WriteAllText(localFile, "v1");
        File.SetLastWriteTimeUtc(localFile, Utc(2, 10));

        var service = new SettingsSyncService(null, local);
        var first = await service.SyncAsync(settings);

        // Вторая синхронизация: версии одинаковы (время перенесено) → ничего не передаём.
        var second = await service.SyncAsync(settings);
        Assert.True(second.Success);
        Assert.Empty(second.ChangedFiles);
        Assert.Contains("settings.json", first.ChangedFiles);
    }

    [Fact]
    public async Task Sync_FolderStorage_DownloadsNewerRemote_AndCreatesConflictBackup()
    {
        var local = Dir("sync_local3");
        var remote = Dir("sync_remote3");
        var settings = FolderSettings(remote, "settings.json");

        var localFile = Path.Combine(local, "settings.json");
        File.WriteAllText(localFile, "LOCAL");
        File.SetLastWriteTimeUtc(localFile, Utc(1, 6));

        var remoteFile = Path.Combine(remote, "settings.json");
        File.WriteAllText(remoteFile, "REMOTE");
        File.SetLastWriteTimeUtc(remoteFile, Utc(3, 18));

        var service = new SettingsSyncService(null, local);
        var log = new StringBuilder();
        var result = await service.SyncAsync(settings, line => log.AppendLine(line));

        Assert.True(result.Success);
        Assert.Contains("settings.json", result.ChangedFiles);
        Assert.Equal("REMOTE", File.ReadAllText(localFile));

        var conflicts = Path.Combine(local, SettingsSyncService.ConflictsSubDir);
        var backups = Directory.Exists(conflicts) ? Directory.GetFiles(conflicts) : Array.Empty<string>();
        var conflict = Assert.Single(backups);
        Assert.Contains("settings.json.local.conflict-", Path.GetFileName(conflict));
        Assert.Equal("LOCAL", File.ReadAllText(conflict));

        // После скачивания LocalTime = remote time → больше ничего не передаём.
        var second = await service.SyncAsync(settings);
        Assert.Empty(second.ChangedFiles);
    }

    [Fact]
    public async Task Sync_FolderStorage_BacksUpRemoteBeforeUpload()
    {
        var local = Dir("sync_local4");
        var remote = Dir("sync_remote4");
        var settings = FolderSettings(remote, "settings.json");

        var remoteFile = Path.Combine(remote, "settings.json");
        File.WriteAllText(remoteFile, "OLD-REMOTE");
        File.SetLastWriteTimeUtc(remoteFile, Utc(1, 5));

        var localFile = Path.Combine(local, "settings.json");
        File.WriteAllText(localFile, "NEW-LOCAL");
        File.SetLastWriteTimeUtc(localFile, Utc(4, 20));

        var service = new SettingsSyncService(null, local);
        var result = await service.SyncAsync(settings);

        Assert.True(result.Success);
        Assert.Equal("NEW-LOCAL", File.ReadAllText(remoteFile));
        var conflictName = Assert.Single(result.Conflicts);
        Assert.Contains("settings.json", conflictName);
        Assert.Contains(".remote.conflict-", conflictName);
        var backup = Path.Combine(local, SettingsSyncService.ConflictsSubDir, conflictName);
        Assert.Equal("OLD-REMOTE", File.ReadAllText(backup));
    }

    [Fact]
    public async Task Sync_PasswordNeverAppearsInLog()
    {
        var local = Dir("sync_local5");
        var remote = Dir("sync_remote5");
        var settings = FolderSettings(remote, "settings.json");
        settings.User = "sync-user";
        // Пароль шифруем фиксированным ключом (как машина машины).
        var crypto = new NoteCrypto(new FixedKeyProvider(new byte[32]));
        var service = new SettingsSyncService(null, local, crypto);
        settings.PasswordEncrypted = service.EncryptPassword("SUPER-SECRET-PASS");

        var localFile = Path.Combine(local, "settings.json");
        File.WriteAllText(localFile, "v1");
        File.SetLastWriteTimeUtc(localFile, Utc(2, 10));

        var log = new StringBuilder();
        var result = await service.SyncAsync(settings, line => log.AppendLine(line));
        Assert.True(result.Success);
        Assert.DoesNotContain("SUPER-SECRET-PASS", log.ToString());
        Assert.DoesNotContain("SUPER-SECRET-PASS", result.Message);
    }

    [Fact]
    public void Crypto_RoundTrip_PasswordEncrypted()
    {
        var crypto = new NoteCrypto(new FixedKeyProvider(new byte[32]));
        var service = new SettingsSyncService(null, Dir("sync_local6"), crypto);

        var cipher = service.EncryptPassword("пароль-123");
        Assert.StartsWith("v2:", cipher);
        Assert.DoesNotContain("пароль-123", cipher);
        Assert.Equal("пароль-123", service.DecryptPassword(new SettingsSyncSettings { PasswordEncrypted = cipher }));
    }

    // ---------- Расписание ----------

    [Fact]
    public void Runner_IsDue_RespectsInterval()
    {
        var settings = new SettingsSyncSettings { Enabled = true, IntervalMinutes = 60, LastSyncAt = null };
        Assert.True(SettingsSyncRunner.IsDue(settings, new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)));

        settings.LastSyncAt = new DateTime(2026, 1, 1, 11, 0, 0, DateTimeKind.Utc);
        Assert.False(SettingsSyncRunner.IsDue(settings, new DateTime(2026, 1, 1, 11, 30, 0, DateTimeKind.Utc)));
        Assert.True(SettingsSyncRunner.IsDue(settings, new DateTime(2026, 1, 1, 12, 1, 0, DateTimeKind.Utc)));

        settings.Enabled = false;
        Assert.False(SettingsSyncRunner.IsDue(settings, new DateTime(2026, 1, 2, 12, 0, 0, DateTimeKind.Utc)));
        settings.Enabled = true;
        settings.IntervalMinutes = 0;
        Assert.False(SettingsSyncRunner.IsDue(settings, new DateTime(2026, 1, 2, 12, 0, 0, DateTimeKind.Utc)));
    }

    // ---------- Хелперы ----------

    private static SettingsSyncSettings FolderSettings(string remoteFolder, params string[] patterns) => new()
    {
        Enabled = true,
        StorageKind = SettingsSyncStorageKind.NetworkFolder,
        FolderPath = remoteFolder,
        IncludePatterns = patterns.ToList()
    };

    private sealed class FixedKeyProvider(byte[] key) : INoteCryptoKeyProvider
    {
        public byte[]? GetKey() => key;
        public string Describe() => "test";
    }
}