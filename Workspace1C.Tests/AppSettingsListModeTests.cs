using System;
using System.IO;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты запоминания выбранной вкладки списка баз (issue #52): единый режим
/// <see cref="AppSettings.ListMode"/> (All/Favorites/Recent/Running) мигрируется из
/// старых флагов <see cref="AppSettings.ShowFavoritesOnly"/>/<see cref="AppSettings.ShowRunningOnly"/>,
/// по умолчанию равен «Все базы» и переживает сохранение/загрузку профиля.
/// </summary>
public sealed class AppSettingsListModeTests : IDisposable
{
    private readonly string _tempDir;

    public AppSettingsListModeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cm_list_mode_tests_" + Guid.NewGuid().ToString("N"));
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
            // Игнорируем: каталог мог быть занят или уже удалён.
        }
    }

    // ---- Миграция из старых флагов ----

    [Fact]
    public void NormalizeListMode_MigratesFavoritesFlag()
    {
        var settings = new AppSettings { ShowFavoritesOnly = true };

        var mode = settings.NormalizeListMode();

        Assert.Equal(ListViewModes.Favorites, mode);
        Assert.Equal(ListViewModes.Favorites, settings.ListMode);
    }

    [Fact]
    public void NormalizeListMode_MigratesRunningFlag()
    {
        var settings = new AppSettings { ShowRunningOnly = true };

        var mode = settings.NormalizeListMode();

        Assert.Equal(ListViewModes.Running, mode);
        Assert.Equal(ListViewModes.Running, settings.ListMode);
    }

    [Fact]
    public void NormalizeListMode_RunningTakesPriorityOverFavorites()
    {
        // Оба старых флага одновременно: «Запущенные» приоритетнее (как было в WPF-загрузке).
        var settings = new AppSettings { ShowFavoritesOnly = true, ShowRunningOnly = true };

        Assert.Equal(ListViewModes.Running, settings.NormalizeListMode());
    }

    [Fact]
    public void NormalizeListMode_EmptyDefaultsToAll()
    {
        var settings = new AppSettings();

        Assert.Equal(ListViewModes.All, settings.NormalizeListMode());
        Assert.Equal(ListViewModes.All, settings.ListMode);
    }

    [Fact]
    public void NormalizeListMode_KeepsExistingValue()
    {
        // Режим уже выбран (например, «Недавние») — старые флаги его не перетирают.
        var settings = new AppSettings { ListMode = ListViewModes.Recent, ShowFavoritesOnly = true };

        Assert.Equal(ListViewModes.Recent, settings.NormalizeListMode());
        Assert.Equal(ListViewModes.Recent, settings.ListMode);
    }

    [Fact]
    public void NormalizeListMode_UnknownValueFallsBackToAll()
    {
        var settings = new AppSettings { ListMode = "Bogus" };

        Assert.Equal(ListViewModes.All, settings.NormalizeListMode());
        Assert.Equal(ListViewModes.All, settings.ListMode);
    }

    [Fact]
    public void NormalizeListMode_IsIdempotent()
    {
        var settings = new AppSettings { ShowFavoritesOnly = true };

        settings.NormalizeListMode();
        settings.NormalizeListMode();

        Assert.Equal(ListViewModes.Favorites, settings.ListMode);
    }

    [Fact]
    public void NormalizeForLoad_MigratesLegacyFlags()
    {
        // Легаси-файл без поля ListMode: NormalizeForLoad (вызывается в LoadSettings)
        // выводит режим из старых флагов.
        var settings = new AppSettings { ShowRunningOnly = true };
        settings.NormalizeForLoad();

        Assert.Equal(ListViewModes.Running, settings.ListMode);
    }

    // ---- Разбор канонических строк ----

    [Theory]
    [InlineData("All", ListViewMode.All)]
    [InlineData("Favorites", ListViewMode.Favorites)]
    [InlineData("Recent", ListViewMode.Recent)]
    [InlineData("Running", ListViewMode.Running)]
    public void ListViewModes_Parse_AllModes(string value, ListViewMode expected)
    {
        Assert.Equal(expected, ListViewModes.Parse(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Bogus")]
    public void ListViewModes_Parse_UnknownFallsBackToAll(string? value)
    {
        Assert.Equal(ListViewMode.All, ListViewModes.Parse(value));
    }

    [Theory]
    [InlineData(ListViewMode.All, "All")]
    [InlineData(ListViewMode.Favorites, "Favorites")]
    [InlineData(ListViewMode.Recent, "Recent")]
    [InlineData(ListViewMode.Running, "Running")]
    public void ListViewModes_ToSettingString_AllModes(ListViewMode mode, string expected)
    {
        Assert.Equal(expected, ListViewModes.ToSettingString(mode));
    }

    // ---- Сохранение/загрузка каждого режима через репозиторий ----

    [Theory]
    [InlineData("All")]
    [InlineData("Favorites")]
    [InlineData("Recent")]
    [InlineData("Running")]
    public void AppSettings_ListMode_SurvivesSaveAndLoad(string mode)
    {
        var repo = new InfobaseRepository(directory: _tempDir);
        repo.SaveSettings(new AppSettings { ListMode = mode });

        var loaded = repo.LoadSettings();

        Assert.Equal(mode, loaded.ListMode);
    }

    [Fact]
    public void AppSettings_ListMode_DefaultsToAllForNewProfile()
    {
        var repo = new InfobaseRepository(directory: _tempDir);
        repo.SaveSettings(new AppSettings());

        var loaded = repo.LoadSettings();

        Assert.Equal(ListViewModes.All, loaded.ListMode);
    }
}