using System;
using System.IO;
using System.Text.RegularExpressions;
using Workspace1C.Models;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #61 («Буквы как были темные у заголовков поле ввода - так и остались»): стражи на
/// единую кисть подписи полей форм <c>FormLabelBrush</c> — она определена в обеих темах
/// обеих платформ (WPF *.xaml и Avalonia *.axaml), привязана к первичному тексту темы и
/// используется построителями окон (<c>WpfUi.Label</c>/<c>AvaloniaUi.Label</c>) вместо
/// фиксированных цветов, которые в тёмной теме могли оставаться тёмными.
/// </summary>
public sealed class ThemeFormLabelTests
{
    private static string ReadProjectFile(string relativePath)
    {
        var root = FindRepoRoot();
        var fullPath = Path.Combine(root, relativePath);
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Theory]
    [InlineData("LightTheme.xaml")]
    [InlineData("DarkTheme.xaml")]
    [InlineData("LightTheme.axaml")]
    [InlineData("DarkTheme.axaml")]
    public void FormLabelBrush_DefinedInEveryTheme(string themeFile)
    {
        var theme = ReadProjectFile(Path.Combine("Workspace1C", "Themes", themeFile));

        // Кисть объявлена и привязана к первичному цвету текста темы (контрастен на фоне
        // карточек/полей ввода и в тёмной, и в светлой теме; инвариант ко всем схемам).
        Assert.Contains("x:Key=\"FormLabelBrush\"", theme, StringComparison.Ordinal);
        Assert.Contains("FormLabelBrush\" Color=\"{DynamicResource TextPrimaryColor}\"", theme, StringComparison.Ordinal);
    }

    [Fact]
    public void WpfUi_Label_UsesFormLabelBrush()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "WpfUi.cs"));

        Assert.Contains("Label(", code);
        Assert.Contains("\"FormLabelBrush\"", code);
    }

    [Fact]
    public void AvaloniaUi_Label_UsesFormLabelBrush()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "AvaloniaUi.Avalonia.cs"));

        Assert.Contains("Label(", code);
        Assert.Contains("\"FormLabelBrush\"", code);
    }

    [Fact]
    public void WpfUi_FieldLabelAndSection_UseFormLabelBrush()
    {
        // Issue #61 (1.0.0.75): единая точка для подписей полей (FieldLabel) и заголовков
        // секций (Section) — обе кисть FormLabelBrush, чтобы код-окна не оставались чёрными.
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "WpfUi.cs"));
        Assert.Contains("public static TextBlock FieldLabel(", code);
        Assert.Contains("public static TextBlock Section(", code);
        var count = Regex.Matches(code, "FormLabelBrush").Count;
        Assert.True(count >= 3, $"Ожидалось ≥3 вхождений FormLabelBrush в WpfUi (Label/FieldLabel/Section), найдено: {count}");
    }

    [Fact]
    public void AvaloniaUi_FieldLabelAndSection_UseFormLabelBrush()
    {
        // Issue #61 (1.0.0.75): FieldLabel/Section в AvaloniaUi привязывают к FormLabelBrush.
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "AvaloniaUi.Avalonia.cs"));
        Assert.Contains("public static TextBlock FieldLabel(", code);
        Assert.Contains("public static TextBlock Section(", code);
        var count = Regex.Matches(code, "FormLabelBrush").Count;
        Assert.True(count >= 3, $"Ожидалось ≥3 вхождений FormLabelBrush в AvaloniaUi (Label/FieldLabel/Section), найдено: {count}");
    }

    [Theory]
    [InlineData("#3B82F6")]
    [InlineData("#F59E0B")]
    [InlineData("#10B981")]
    [InlineData("#7C4DFF")]
    public void GeneratedSchemes_IncludeFormLabelBrushMatchingTextPrimary(string baseHex)
    {
        // Генерируемые схемы (issue #271) обязаны нести ключ FormLabelBrush
        // со значением первичного текста — иначе в тёмной теме подписи остаются
        // чёрными (исходный дефект issue #61 для пользовательских схем).
        var scheme = ColorScheme.GenerateFromColor(baseHex);

        Assert.True(scheme.LightColors.TryGetValue("FormLabelBrush", out var lightLabel), "Светлая палитра не содержит FormLabelBrush");
        Assert.Equal(scheme.LightColors["TextPrimaryColor"], lightLabel);

        Assert.True(scheme.DarkColors.TryGetValue("FormLabelBrush", out var darkLabel), "Тёмная палитра не содержит FormLabelBrush");
        Assert.Equal(scheme.DarkColors["TextPrimaryColor"], darkLabel);
    }

    [Fact]
    public void BuiltInSchemes_FormLabelContrastOnCardsAndInputs_MeetsWcag()
    {
        // Подпись поля формы контрастна (WCAG AA ≥ 4.5:1) на фоне карточки и поля ввода
        // в обеих темах обеих платформ — инвариант ко всем схемам.
        foreach (var scheme in new[] { ColorScheme.CreateLight(), ColorScheme.CreateDark() })
        {
            foreach (var dark in new[] { false, true })
            {
                var palette = scheme.Palette(dark);
                var label = palette["FormLabelBrush"];
                Assert.True(ColorScheme.ContrastRatio(label, palette["CardBackgroundColor"]) >= 4.5,
                    $"Схема «{scheme.Name}» (dark={dark}): FormLabelBrush {label} на карточке {palette["CardBackgroundColor"]} < 4.5:1");
                Assert.True(ColorScheme.ContrastRatio(label, palette["InputBackgroundColor"]) >= 4.5,
                    $"Схема «{scheme.Name}» (dark={dark}): FormLabelBrush {label} на поле ввода {palette["InputBackgroundColor"]} < 4.5:1");
            }
        }
    }

    [Theory]
    [InlineData("#3B82F6")]
    [InlineData("#F59E0B")]
    [InlineData("#10B981")]
    public void GeneratedSchemes_FormLabelContrastOnCardsAndInputs_MeetsWcag(string baseHex)
    {
        var scheme = ColorScheme.GenerateFromColor(baseHex);
        foreach (var dark in new[] { false, true })
        {
            var palette = scheme.Palette(dark);
            var label = palette["FormLabelBrush"];
            Assert.True(ColorScheme.ContrastRatio(label, palette["CardBackgroundColor"]) >= 4.5,
                $"Сгенерированная {baseHex} (dark={dark}): FormLabelBrush {label} на карточке {palette["CardBackgroundColor"]} < 4.5:1");
            Assert.True(ColorScheme.ContrastRatio(label, palette["InputBackgroundColor"]) >= 4.5,
                $"Сгенерированная {baseHex} (dark={dark}): FormLabelBrush {label} на поле ввода {palette["InputBackgroundColor"]} < 4.5:1");
        }
    }

    [Fact]
    public void NormalizedLegacyScheme_IncludesFormLabelBrush()
    {
        // Старые/пользовательские схемы после Normalize() получают ключ FormLabelBrush
        // из палитры по умолчанию (для обеих платформ) — подписи не «теряют» кисть.
        var scheme = new ColorScheme { Name = "Старая" };
        scheme.LightColors["TextPrimaryColor"] = "#121212";
        scheme.LightColors["CardBackgroundColor"] = "#FFFFFF";
        scheme.DarkColors["TextPrimaryColor"] = "#F1F5F9";
        scheme.DarkColors["CardBackgroundColor"] = "#1E293B";
        scheme.Normalize();

        Assert.Equal("#121212", scheme.LightColors["FormLabelBrush"]);
        Assert.Equal("#F1F5F9", scheme.DarkColors["FormLabelBrush"]);
    }
}