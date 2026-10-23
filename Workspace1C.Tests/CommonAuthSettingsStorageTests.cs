using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Хранение и миграция раздельной общей авторизации по режимам (issue #57):
/// старый settings.json только с legacy <see cref="AppSettings.CommonAuthorization"/>
/// загружается без потерь (раздельные общие отсутствуют), а новое сохранение пишет
/// заданные раздельные поля <see cref="AppSettings.CommonEnterpriseAuth"/> /
/// <see cref="AppSettings.CommonConfiguratorAuth"/> наравне с legacy.
/// </summary>
public sealed class CommonAuthSettingsStorageTests : IDisposable
{
    private readonly string _tempDir;

    public CommonAuthSettingsStorageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cm_common_auth_settings_" + Guid.NewGuid().ToString("N"));
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

    private string SettingsPath => Path.Combine(_tempDir, "settings.json");

    private static InfobaseAuthSettings Auth(string user, string password,
        AuthenticationMode mode = AuthenticationMode.Credentials)
        => new() { AuthenticationMode = mode, User = user, Password = password };

    [Fact]
    public void SaveAndLoad_PreservesLegacyAndSplitCommonAuth()
    {
        var repo = new InfobaseRepository(directory: _tempDir);
        repo.SaveSettings(new AppSettings
        {
            CommonAuthorization = Auth("legacyU", "legacyP"),
            CommonEnterpriseAuth = Auth("entU", "entP"),
            CommonConfiguratorAuth = Auth("cfgU", "cfgP")
        });

        var loaded = repo.LoadSettings();

        Assert.NotNull(loaded.CommonAuthorization);
        Assert.Equal("legacyU", loaded.CommonAuthorization!.User);
        Assert.NotNull(loaded.CommonEnterpriseAuth);
        Assert.Equal("entU", loaded.CommonEnterpriseAuth!.User);
        Assert.Equal("entP", loaded.CommonEnterpriseAuth!.Password);
        Assert.NotNull(loaded.CommonConfiguratorAuth);
        Assert.Equal("cfgU", loaded.CommonConfiguratorAuth!.User);
        Assert.Equal("cfgP", loaded.CommonConfiguratorAuth!.Password);
    }

    [Fact]
    public void SaveSettings_WritesGivenSplitFieldsToJson()
    {
        // Заданные раздельные поля пишутся в файл (DefaultIgnoreCondition.WhenWritingNull
        // НЕ удаляет их); legacy тоже остаётся, если задан.
        var repo = new InfobaseRepository(directory: _tempDir);
        repo.SaveSettings(new AppSettings
        {
            CommonAuthorization = Auth("legacyU", "legacyP"),
            CommonEnterpriseAuth = Auth("entU", "entP")
        });

        var json = File.ReadAllText(SettingsPath);

        Assert.Contains("\"CommonAuthorization\"", json);
        Assert.Contains("\"CommonEnterpriseAuth\"", json);
        Assert.Contains("\"entU\"", json);
        // CommonConfiguratorAuth не задан — поле в файл не пишется (WhenWritingNull).
        Assert.DoesNotContain("CommonConfiguratorAuth", json);
        Assert.DoesNotContain("\"cfgU\"", json);
    }

    [Fact]
    public void OldSettings_OnlyLegacyCommonAuth_LoadsWithoutLoss()
    {
        // settings.json, созданный версией до раздельной по режимам общей авторизации:
        // содержит только CommonAuthorization. Загружается без потерь, раздельные
        // общие отсутствуют (null).
        // SchemaVersion не задан (0 ≤ поддерживаемой версии): файл читается как есть,
        // как любой старый settings.json, созданный до раздельной по режимам авторизации.
        File.WriteAllText(SettingsPath, """
        {
          "CommonAuthorization": { "AuthenticationMode": 1, "User": "oldU", "Password": "oldP" }
        }
        """);

        var loaded = new InfobaseRepository(directory: _tempDir).LoadSettings();

        Assert.NotNull(loaded.CommonAuthorization);
        Assert.Equal("oldU", loaded.CommonAuthorization!.User);
        Assert.Equal("oldP", loaded.CommonAuthorization!.Password);
        Assert.Null(loaded.CommonEnterpriseAuth);
        Assert.Null(loaded.CommonConfiguratorAuth);
    }

    [Fact]
    public void EmptySettings_SplitCommonAuthNullByDefault()
    {
        // Пустой/новый профиль: раздельные общие не заданы — резолвер продолжит
        // использовать legacy (обратная совместимость).
        var repo = new InfobaseRepository(directory: _tempDir);
        repo.SaveSettings(new AppSettings());

        var loaded = repo.LoadSettings();

        Assert.Null(loaded.CommonAuthorization);
        Assert.Null(loaded.CommonEnterpriseAuth);
        Assert.Null(loaded.CommonConfiguratorAuth);
    }

    [Fact]
    public void DefaultSplitCommonAuth_IsDefault_AndIgnoredByResolver()
    {
        // Раздельная общая со значениями по умолчанию (IsDefault) осмысленно читается
        // из файла, но резолвер считает её незаданной (fallback на legacy).
        var legacy = Auth("legacyU", "legacyP");
        var split = new InfobaseAuthSettings(); // IsDefault == true
        Assert.True(split.IsDefault);

        InfobaseAuthResolver.Resolve(new Infobase { Name = "База" }, OneCLaunchMode.Enterprise,
            out var mode, out var user, out var pass, split, null, legacy);

        Assert.Equal(AuthenticationMode.Credentials, mode);
        Assert.Equal("legacyU", user);
    }
}
