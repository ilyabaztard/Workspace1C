using System.Text.Json;
using Workspace1C.Models;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты настройки правки меню трея (issue #62): AppSettings.TrayMenuEditingEnabled
/// переживает сохранение/загрузку JSON-профиля (как в InfobaseRepository.LoadSettings).
/// </summary>
public sealed class TrayMenuAppSettingsTests
{
    [Fact]
    public void AppSettings_SaveLoad_TrayMenuEditingEnabled_Roundtrip()
    {
        var settings = new AppSettings { TrayMenuEditingEnabled = true };

        var json = JsonSerializer.Serialize(settings);
        var loaded = JsonSerializer.Deserialize<AppSettings>(json)!;

        Assert.True(loaded.TrayMenuEditingEnabled);
    }

    [Fact]
    public void AppSettings_Default_TrayMenuEditingEnabled_False()
    {
        var settings = new AppSettings();

        Assert.False(settings.TrayMenuEditingEnabled);
    }
}
