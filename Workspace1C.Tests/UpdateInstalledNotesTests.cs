using System.IO;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #44 (комментарий 7OH 23.10): уведомление «Приложение обновлено до версии X»
/// (почта/Telegram) содержит описание изменений выпуска.
/// </summary>
public sealed class UpdateInstalledNotesTests
{
    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "w1c_upd_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Marker_KeepsReleaseNotesAndUrl()
    {
        var store = new PendingUpdateStore(directoryOverride: TempDir());
        store.Write("v1.0.0.60", "### Исправлено\n- пункт", "https://example.org/r");

        var marker = store.ReadMarkerAndClear();

        Assert.NotNull(marker);
        Assert.Equal("v1.0.0.60", marker!.Version);
        Assert.Contains("пункт", marker.ReleaseNotes);
        Assert.Equal("https://example.org/r", marker.ReleaseUrl);
        Assert.Null(store.ReadMarkerAndClear());
    }

    [Fact]
    public void LegacyMarker_WithoutNotes_IsRead()
    {
        var dir = TempDir();
        File.WriteAllText(Path.Combine(dir, PendingUpdateStore.FileName), "{\"Version\":\"v1.0.0.56\"}");
        var store = new PendingUpdateStore(directoryOverride: dir);

        var marker = store.ReadMarkerAndClear();

        Assert.NotNull(marker);
        Assert.Equal("v1.0.0.56", marker!.Version);
        Assert.Equal(string.Empty, marker.ReleaseNotes);
    }

    [Fact]
    public void ReadAndClear_StillReturnsVersion()
    {
        var store = new PendingUpdateStore(directoryOverride: TempDir());
        store.Write("v1.0.0.60");
        Assert.Equal("v1.0.0.60", store.ReadAndClear());
    }

    [Fact]
    public void PrepareNotes_DropsReleaseBoilerplate()
    {
        var body = "# Workspace 1C 1.0.0.60\n\nРелиз доступен для **Windows** (WPF) и **Linux** (Avalonia).\n\n### Исправлено\n\n- Описание в уведомлении\n\nПодробности — в [CHANGELOG.md](https://x).";

        var notes = UpdateNotificationText.PrepareNotes(body);

        Assert.DoesNotContain("# Workspace 1C", notes);
        Assert.DoesNotContain("Релиз доступен", notes);
        Assert.DoesNotContain("CHANGELOG", notes);
        Assert.StartsWith("### Исправлено", notes);
        Assert.Contains("- Описание в уведомлении", notes);
    }

    [Fact]
    public void PrepareNotes_Truncated()
    {
        var notes = UpdateNotificationText.PrepareNotes(new string('а', 5000));
        Assert.True(notes.Length <= UpdateNotificationText.MaxNotesLength + 1);
        Assert.EndsWith("…", notes);
    }

    [Fact]
    public void InstalledMessage_ContainsNotesAndUrl()
    {
        var message = UpdateNotificationText.BuildInstalledMessage("v1.0.0.60", "- пункт изменений", "https://example.org/r");
        Assert.Contains("- пункт изменений", message);
        Assert.Contains("https://example.org/r", message);
    }

    [Fact]
    public void InstalledMessage_WithoutNotes_IsHeaderOnly()
    {
        var withNotes = UpdateNotificationText.BuildInstalledMessage("v1.0.0.60", "- пункт", null);
        var without = UpdateNotificationText.BuildInstalledMessage("v1.0.0.60", "  ", null);
        Assert.True(without.Length < withNotes.Length);
        Assert.DoesNotContain("пункт", without);
    }

    [Fact]
    public void AppStartup_UsesNotesInInstalledNotification()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Workspace1C", "Views")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        foreach (var file in new[] { "App.xaml.cs", "App.axaml.cs" })
        {
            var code = File.ReadAllText(Path.Combine(dir!.FullName, "Workspace1C", file));
            Assert.Contains("UpdateNotificationText.BuildInstalledMessage", code);
            Assert.Contains("GetReleaseNotesByTagAsync", code);
        }
    }
}
