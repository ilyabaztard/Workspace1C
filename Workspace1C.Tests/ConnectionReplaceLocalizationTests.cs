using System.Text.RegularExpressions;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Регресс-тест рассинхронизации локализации окна «Замена строк подключения»
/// (issue #357): выпущенная сборка 0.3.10.3 не содержала ключей
/// ConnectionReplace.FieldLabel/ScopeLabel/ModeLabel во встроенных JSON, и у
/// пользователей с внешним устаревшим Languages/ru.json подписи полей показывались
/// «как есть» ключами. Тест извлекает ВСЕ используемые окном ключи из XAML (обеих
/// разметок) и кода (WPF/Avalonia/ViewModel) и ассертит, что КАЖДЫЙ есть и во
/// встроенном ru.json, и во встроенном en.json (те же файлы встраиваются в сборку
/// как EmbeddedResource Localization\Languages\*.json).
/// </summary>
public sealed class ConnectionReplaceLocalizationTests
{
    /// <summary>Возвращает корень репозитория (как в SettingsWindowXamlResourcesTests).</summary>
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

    private static string ReadProjectFile(string relativePath)
    {
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
    }

    /// <summary>Файлы окна и его ViewModel, где используются ключи ConnectionReplace.*.</summary>
    private static readonly string[] SourceFiles =
    {
        Path.Combine("Workspace1C", "Views", "ConnectionReplaceWindow.xaml"),
        Path.Combine("Workspace1C", "Views", "ConnectionReplaceWindow.xaml.cs"),
        Path.Combine("Workspace1C", "Views", "ConnectionReplaceWindow.Avalonia.cs"),
        Path.Combine("Workspace1C", "ViewModels", "ConnectionReplaceViewModel.cs"),
        Path.Combine("Workspace1C", "ViewModels", "MainViewModel.ConnectionReplace.cs"),
    };

    private static HashSet<string> CollectUsedKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in SourceFiles)
        {
            var content = ReadProjectFile(file);
            if (file.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            {
                // XAML: {loc:Loc ConnectionReplace.FieldLabel}.
                foreach (Match m in Regex.Matches(content, @"loc:Loc\s+(ConnectionReplace\.[A-Za-z0-9_.]+)"))
                    keys.Add(m.Groups[1].Value);
            }
            else
            {
                // Код: ключи используются СТРОКОВЫМИ литералами
                // T("ConnectionReplace.Fields.Server") — комментарии и имена файлов
                // в строковые литералы не попадают.
                foreach (Match m in Regex.Matches(
                             content, @"[""'](ConnectionReplace\.[A-Za-z0-9_]+(?:\.[A-Za-z0-9_]+)*)[""']"))
                    keys.Add(m.Groups[1].Value);
            }
        }

        return keys;
    }

    private static HashSet<string> CollectDictionaryKeys(string relativePath)
    {
        var content = ReadProjectFile(relativePath);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(content, @"""(ConnectionReplace\.[A-Za-z0-9_.]+)""\s*:"))
            keys.Add(m.Groups[1].Value);
        return keys;
    }

    [Fact]
    public void ConnectionReplace_EveryUsedKey_PresentInBothBuiltInDictionaries()
    {
        var used = CollectUsedKeys();
        Assert.True(used.Count >= 25,
            "Ожидалось не менее 25 ключей ConnectionReplace.* в исходниках окна, найдено: " + used.Count);

        var ru = CollectDictionaryKeys(Path.Combine("Workspace1C", "Localization", "Languages", "ru.json"));
        var en = CollectDictionaryKeys(Path.Combine("Workspace1C", "Localization", "Languages", "en.json"));

        var missingInRu = used.Where(k => !ru.Contains(k)).ToList();
        var missingInEn = used.Where(k => !en.Contains(k)).ToList();

        Assert.True(missingInRu.Count == 0,
            "Ключи ConnectionReplace.*, отсутствующие во встроенном ru.json (issue #357): " +
            string.Join(", ", missingInRu));
        Assert.True(missingInEn.Count == 0,
            "Ключи ConnectionReplace.*, отсутствующие во встроенном en.json (issue #357): " +
            string.Join(", ", missingInEn));
    }

    [Theory]
    [InlineData("ConnectionReplace.FieldLabel")]
    [InlineData("ConnectionReplace.ScopeLabel")]
    [InlineData("ConnectionReplace.ModeLabel")]
    public void ConnectionReplace_LabelKeys_MissingInReleasedBuild_ArePresentNow(string key)
    {
        // Именно эти ключи отсутствовали во встроенных JSON сборки 0.3.10.3 (issue #357).
        var ru = CollectDictionaryKeys(Path.Combine("Workspace1C", "Localization", "Languages", "ru.json"));
        var en = CollectDictionaryKeys(Path.Combine("Workspace1C", "Localization", "Languages", "en.json"));
        Assert.Contains(key, ru);
        Assert.Contains(key, en);
    }

    // ---------- Issue #54 (переработка): смена платформы вынесена в отдельное окно ----------

    [Theory]
    [InlineData("ConnectionReplace.ChangePlatform")]
    [InlineData("ConnectionReplace.PlatformLabel")]
    [InlineData("ConnectionReplace.Fields.Platform")]
    [InlineData("ConnectionReplace.Summary.PlatformSuffixFormat")]
    public void ConnectionReplace_PlatformKeys_RemovedInBothDictionaries(string key)
    {
        // Откат (issue #54, комментарий 7OH): смена платформы убрана из окна «Заменить
        // в строках подключения…» — ключи старого функционала не должны остаться ни в одном словаре.
        var ru = CollectDictionaryKeys(Path.Combine("Workspace1C", "Localization", "Languages", "ru.json"));
        var en = CollectDictionaryKeys(Path.Combine("Workspace1C", "Localization", "Languages", "en.json"));
        Assert.DoesNotContain(key, ru);
        Assert.DoesNotContain(key, en);
    }

    [Theory]
    [InlineData("PlatformChange.Title")]
    [InlineData("PlatformChange.PlatformLabel")]
    [InlineData("PlatformChange.SummaryFormat")]
    [InlineData("PlatformChange.AppliedFormat")]
    [InlineData("PlatformChange.ApplyButton")]
    [InlineData("PlatformChange.EmptyList")]
    [InlineData("Main.BatchChangePlatform")]
    public void PlatformChange_Keys_PresentInBothDictionaries(string key)
    {
        // Ключи НОВОГО отдельного окна «Сменить платформу» (issue #54, переработка)
        // и пункта «Для выделенных» обязаны быть в обоих словарях.
        var ru = ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", "ru.json"));
        var en = ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", "en.json"));
        Assert.Contains(string.Format("\"{0}\":", key), ru);
        Assert.Contains(string.Format("\"{0}\":", key), en);
    }

    [Fact]
    public void ConnectionReplace_Names_RevertedToConnectionStringsOnly()
    {
        // Откат названий (issue #54, комментарий 7OH): «Замене строк подключения» возвращён
        // прежний вид — без «и платформу» (как было до 1.0.0.66).
        var ru = ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", "ru.json"));
        var en = ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", "en.json"));

        Assert.Contains("\"Main.BatchConnectionReplace\": \"Заменить в строках подключения…\"", ru);
        Assert.Contains("\"Main.UtilitiesConnectionReplace\": \"Заменить в строках подключения…\"", ru);
        Assert.Contains("\"ConnectionReplace.Title\": \"Заменить в строках подключения…\"", ru);

        Assert.Contains("\"Main.BatchConnectionReplace\": \"Replace in connection strings…\"", en);
        Assert.Contains("\"Main.UtilitiesConnectionReplace\": \"Replace in connection strings…\"", en);
        Assert.Contains("\"ConnectionReplace.Title\": \"Replace in base connection strings…\"", en);

        // Старые названия (с упоминанием платформы) не должны встречаться в словарях.
        Assert.DoesNotContain("\"Заменить строки подключения и платформу…\"", ru);
        Assert.DoesNotContain("\"Replace connection strings and platform…\"", en);
    }

    [Fact]
    public void ConnectionReplace_DictionariesSameKeySet()
    {
        // Словари ru/en должны содержать одинаковый набор ключей окна —
        // отсутствие ключа в одном из них означает «ключ вместо текста» в этом языке.
        var ru = CollectDictionaryKeys(Path.Combine("Workspace1C", "Localization", "Languages", "ru.json"));
        var en = CollectDictionaryKeys(Path.Combine("Workspace1C", "Localization", "Languages", "en.json"));
        Assert.Equal(ru, en);
    }

    // ---------- Issue #357: закрытый ComboBox рендерит выбранный элемент ----------

    /// <summary>
    /// WPF XAML окна обязан рендерить закрытую часть комбобоксов «Поле»/«Область»/
    /// «Режим» тем же шаблоном, что и элементы списка: DisplayMemberPath заменён на
    /// явный ItemTemplate с {Binding DisplayText} — по одному на каждый ComboBox.
    /// </summary>
    [Fact]
    public void ConnectionReplaceWindowXaml_ComboboxesBindDisplayTextViaItemTemplate()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ConnectionReplaceWindow.xaml"));

        // Явный шаблон выбранного значения/элементов — три комбобокса, три шаблона.
        Assert.Contains("ItemTemplate", xaml, StringComparison.Ordinal);
        Assert.Equal(3, Regex.Matches(xaml, @"Text=""\{Binding DisplayText\}""").Count);
        // DisplayMemberPath больше не используется: он конфликтует с SelectedValuePath
        // и приводил к ToString()-рендерингу ключа в закрытой части.
        Assert.DoesNotContain("DisplayMemberPath", xaml, StringComparison.Ordinal);
    }

    /// <summary>
    /// Avalonia-версия BuildCombo задаёт SelectionBoxItemTemplate (тот же FuncDataTemplate,
    /// что и ItemTemplate): без него закрытая часть ComboBox рендерит ToString() элемента.
    /// </summary>
    [Fact]
    public void ConnectionReplaceWindowAvalonia_BuildComboSetsSelectionBoxItemTemplate()
    {
        var source = ReadProjectFile(Path.Combine(
            "Workspace1C", "Views", "ConnectionReplaceWindow.Avalonia.cs"));

        Assert.Contains("combo.ItemTemplate", source, StringComparison.Ordinal);
        Assert.Contains("combo.SelectionBoxItemTemplate", source, StringComparison.Ordinal);
    }
}
