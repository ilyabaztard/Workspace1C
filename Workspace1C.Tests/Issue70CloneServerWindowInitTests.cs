using System;
using System.IO;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты-стражи диалога «Дублировать серверную базу» (issue #70 «Дублировать базу —
/// ошибка»): при открытии окна падала NullReferenceException, потому что RadioButton
/// ModeFullRadio объявлен в XAML с IsChecked="True" и Checked="OnModeChanged", а TextBlock
/// HintText — ниже по разметке; событие Checked во время InitializeComponent срабатывало
/// раньше создания HintText, и UpdateModeHint() обращался к null-элементу.
/// Стражи проверяют наличие защиты от раннего вызова в WPF-коде
/// (<c>CloneServerInfobaseWindow.xaml.cs</c>) и в Avalonia-построителе
/// (<c>CloneServerInfobaseWindow.Avalonia.cs</c>), а также локализацию ключей подсказки.
/// </summary>
public sealed class Issue70CloneServerWindowInitTests
{
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

    [Fact]
    public void WpfWindow_XamlDeclaresCheckedRadioBeforeHintText()
    {
        // Сценарий issue #70: ModeFullRadio с IsChecked="True" и Checked="OnModeChanged"
        // объявлен в разметке раньше, чем TextBlock HintText — без защиты в коде событие
        // Checked при InitializeComponent приводило к NullReferenceException.
        var xaml = ReadProjectFile("Workspace1C", "Views", "CloneServerInfobaseWindow.xaml");

        var modeFullIndex = xaml.IndexOf("ModeFullRadio", StringComparison.Ordinal);
        var checkedIndex = xaml.IndexOf("Checked=\"OnModeChanged\"", StringComparison.Ordinal);
        var hintIndex = xaml.IndexOf("HintText", StringComparison.Ordinal);

        Assert.True(modeFullIndex >= 0, "Радиокнопка ModeFullRadio должна быть в разметке.");
        Assert.True(checkedIndex >= 0, "Обработчик Checked=\"OnModeChanged\" должен быть в разметке.");
        Assert.True(hintIndex >= 0, "TextBlock HintText должен быть в разметке.");
        Assert.True(hintIndex > modeFullIndex,
            "HintText объявлен после ModeFullRadio — событие Checked может сработать до его создания.");
    }

    [Fact]
    public void WpfCode_UpdateModeHint_GuardsNullHintText()
    {
        // Код позади WPF-окна: UpdateModeHint() не обращается к HintText, пока он не создан
        // (guard перед использованием) — фикс issue #70.
        var source = ReadProjectFile("Workspace1C", "Views", "CloneServerInfobaseWindow.xaml.cs");

        var guardIndex = source.IndexOf("HintText is null", StringComparison.Ordinal);
        var useIndex = source.IndexOf("HintText.Text", StringComparison.Ordinal);

        Assert.True(guardIndex >= 0, "В UpdateModeHint() должен быть guard от null HintText.");
        Assert.True(useIndex >= 0, "UpdateModeHint() должен обновлять текст HintText.");
        Assert.True(guardIndex < useIndex,
            "Guard от null HintText должен идти раньше обращения к HintText.");
    }

    [Fact]
    public void AvaloniaCode_UpdateModeHint_GuardsNullHint()
    {
        // Avalonia/Linux-построитель: тот же guard от раннего вызова UpdateModeHint().
        var source = ReadProjectFile("Workspace1C", "Views", "CloneServerInfobaseWindow.Avalonia.cs");

        var guardIndex = source.IndexOf("_hintText is null", StringComparison.Ordinal);
        var useIndex = source.IndexOf("_hintText.Text", StringComparison.Ordinal);

        Assert.True(guardIndex >= 0, "В UpdateModeHint() должен быть guard от null _hintText.");
        Assert.True(useIndex >= 0, "UpdateModeHint() должен обновлять текст _hintText.");
        Assert.True(guardIndex < useIndex,
            "Guard от null _hintText должен идти раньше обращения к _hintText.");
    }

    [Fact]
    public void Localization_ModeHintKeys_ExistInRuAndEn()
    {
        var ru = ReadProjectFile("Workspace1C", "Localization", "Languages", "ru.json");
        var en = ReadProjectFile("Workspace1C", "Localization", "Languages", "en.json");

        foreach (var key in new[] { "CloneServer.ModeFullHint", "CloneServer.ModeConfigHint" })
        {
            Assert.Contains($"\"{key}\"", ru, StringComparison.Ordinal);
            Assert.Contains($"\"{key}\"", en, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Localization_RussianModeHints_AreHumanReadable()
    {
        // Человекочитаемый русский текст подсказок режима копирования.
        using var doc = System.Text.Json.JsonDocument.Parse(
            ReadProjectFile("Workspace1C", "Localization", "Languages", "ru.json"));
        var strings = doc.RootElement.GetProperty("strings");

        Assert.Contains("данные", strings.GetProperty("CloneServer.ModeFullHint").GetString());
        Assert.Contains("конфигураци", strings.GetProperty("CloneServer.ModeConfigHint").GetString());
    }
}
