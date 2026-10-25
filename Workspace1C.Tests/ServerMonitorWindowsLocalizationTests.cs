using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Покрытие локализации окон монитора «Серверы 1С» (issue #3, комментарии 7OH от
/// 2026-10-17 и от 2026-10-18): каждый ключ <c>{loc:Loc ...}</c> в разметке
/// <c>ServerMonitorWindow</c>, <c>RacInfobasePropertiesWindow</c> (2 вкладки) и нового
/// <c>RacInfobaseDropWindow</c> обязан существовать в словарях ru.json И en.json —
/// иначе интерфейс показывает «голый» ключ вместо текста.
/// </summary>
public sealed class ServerMonitorWindowsLocalizationTests
{
    /// <summary>Возвращает корень репозитория (как в LocalizationCoverageServicesTests).</summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Workspace1C", "Views")) &&
                Directory.Exists(Path.Combine(dir.FullName, "Workspace1C.Tests")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Корень репозитория не найден из тестового каталога.");
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var relativePath = Path.Combine(parts);
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
    }

    private static HashSet<string> LoadLanguageKeys(string fileName)
    {
        var json = ReadProjectFile("Workspace1C", "Localization", "Languages", fileName);
        using var doc = JsonDocument.Parse(json);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in doc.RootElement.GetProperty("strings").EnumerateObject())
            keys.Add(property.Name);
        return keys;
    }

    [Theory]
    [InlineData("ServerMonitorWindow.xaml")]
    [InlineData("RacInfobasePropertiesWindow.xaml")]
    [InlineData("RacInfobaseDropWindow.xaml")]
    [InlineData("RacInfobaseDropRetryWindow.xaml")]
    public void WindowXaml_EveryLocKey_ExistsInRuAndEnDictionaries(string fileName)
    {
        var xaml = ReadProjectFile("Workspace1C", "Views", fileName);
        var keys = Regex.Matches(xaml, @"\{loc:Loc\s+([A-Za-z0-9_.]+)\}")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();

        Assert.NotEmpty(keys);
        var ru = LoadLanguageKeys("ru.json");
        var en = LoadLanguageKeys("en.json");

        var missingRu = keys.Where(k => !ru.Contains(k)).ToList();
        var missingEn = keys.Where(k => !en.Contains(k)).ToList();

        Assert.True(missingRu.Count == 0,
            $"{fileName}: ключи локализации не найдены в ru.json: " + string.Join(", ", missingRu));
        Assert.True(missingEn.Count == 0,
            $"{fileName}: ключи локализации не найдены в en.json: " + string.Join(", ", missingEn));
    }

    [Fact]
    public void PropertiesDialog_TwoTabs_AndDropDialog_AndServicesButton_HaveRussianTranslations()
    {
        // 2 вкладки диалога правки базы (18.10 08:51), кнопка «СЛУЖБЫ» (18.10 08:57)
        // и диалог удаления с выбором: ключи обязаны содержать человекочитаемый русский
        // текст в ru.json (в тестовой среде LocalizationManager не инициализирован —
        // словарь читаем напрямую, как тесты покрытия локализации).
        var ru = LoadLanguageValues("ru.json");
        foreach (var key in new[]
                 {
                     "ServerMonitor.Infobase.Props.Tab.Basic",
                     "ServerMonitor.Infobase.Props.Tab.Settings",
                     "ServerMonitor.Servers.Services",
                     "ServerMonitor.Infobase.Drop.OnlyFromCluster",
                     "ServerMonitor.Infobase.Drop.WithDatabase"
                 })
        {
            Assert.True(ru.TryGetValue(key, out var value), $"Нет перевода «{key}» в ru.json");
            Assert.False(string.IsNullOrWhiteSpace(value), $"Пустой перевод «{key}» в ru.json");
            Assert.NotEqual(key, value);
        }
    }

    /// <summary>Словарь «ключ → значение» ru.json/en.json.</summary>
    private static Dictionary<string, string> LoadLanguageValues(string fileName)
    {
        var json = ReadProjectFile("Workspace1C", "Localization", "Languages", fileName);
        using var doc = JsonDocument.Parse(json);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in doc.RootElement.GetProperty("strings").EnumerateObject())
            map[property.Name] = property.Value.GetString() ?? string.Empty;
        return map;
    }
}