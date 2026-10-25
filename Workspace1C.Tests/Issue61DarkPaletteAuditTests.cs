using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Workspace1C.Models;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #61 «Цвета заголовков в темной версии» (последний комментарий 7OH: «…и тёмные
/// заголовки и светлое на светлом выделение»): стражи против фиксированных тёмных
/// цветов текста в WPF-разметке и в Avalonia-коде, а также проверка контраста палитр
/// выделения (ItemSelected/ItemHover против TextPrimary) для встроенных и генерируемых
/// схем. По образцу Issue61TitleColorsTests / ButtonThemeBindingTests.
/// </summary>
public sealed class Issue61DarkPaletteAuditTests
{
    private static readonly string[] ForbiddenDarkHex =
    {
        "#000000", "#000", "#0F172A", "#111827", "#1F2937", "#1C1917", "#0B1120"
    };

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string[] ReadViewFiles(string pattern)
    {
        var viewsDir = Path.Combine(FindRepoRoot(), "Workspace1C", "Views");
        Assert.True(Directory.Exists(viewsDir), $"Каталог Views не найден: {viewsDir}");
        return Directory.GetFiles(viewsDir, pattern)
            .Where(f => !Path.GetFileName(f).StartsWith("_", StringComparison.Ordinal))
            .ToArray();
    }

    private static void AssertNoDarkForegroundHex(string code, string file, bool xamlStyle)
    {
        // Комментарии (XML <!-- … --> и //, /* … */ в коде) не считаем: они лишь описывают
        // проблему и сами по себе не влияют на рендеринг.
        code = Regex.Replace(code, @"<!--.*?-->", string.Empty, RegexOptions.Singleline);
        code = Regex.Replace(code, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

        // XAML: Foreground="#…"; Avalonia-код: Color.Parse("#…") в контексте Foreground.
        var body = xamlStyle
            ? code
            : RemoveNonForegroundParse(code);

        foreach (var hex in ForbiddenDarkHex)
        {
            Assert.DoesNotContain(hex, body, StringComparison.OrdinalIgnoreCase);
            _ = file; // имя файла — для читаемости сбоя
        }
    }

    /// <summary>Вырезает вхождения Color.Parse("#…") вне прямого присваивания Foreground,
    /// чтобы стражи не падали на иконках/фонах (допустимые акцентные/верстальные цвета).</summary>
    private static string RemoveNonForegroundParse(string code)
    {
        // Обрезаем строки, где Color.Parse используется как Background/Icon-кисть: ищем
        // участки «Parse("#…")» и оставляем только те, что в той же строке имеют Foreground.
        var lines = code.Split('\n');
        var kept = new System.Text.StringBuilder();
        foreach (var line in lines)
        {
            if (line.IndexOf("Foreground", StringComparison.Ordinal) >= 0)
                kept.AppendLine(line);
        }
        return kept.ToString();
    }

    [Fact]
    public void WpfXaml_HasNoDarkHardcodedTextForeground()
    {
        foreach (var file in ReadViewFiles("*.xaml"))
            AssertNoDarkForegroundHex(File.ReadAllText(file), file, xamlStyle: true);
    }

    [Fact]
    public void AvaloniaCode_HasNoDarkHardcodedForeground()
    {
        foreach (var file in ReadViewFiles("*.cs"))
        {
            if (!file.Contains(".Avalonia.", StringComparison.Ordinal) && !file.Contains("AvaloniaUi", StringComparison.Ordinal))
                continue;
            AssertNoDarkForegroundHex(File.ReadAllText(file), file, xamlStyle: false);
        }
    }

    [Fact]
    public void AvaloniaTreeBadge_NoFixedBlackFallback()
    {
        // Раньше: text.Foreground = ... ?? "#000000" — черный текст бейджа в тёмной теме.
        // Теперь без явного цвета подпись привязана к кисти темы TextPrimaryBrush (issue #61).
        var code = File.ReadAllText(Path.Combine(FindRepoRoot(), "Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs"));
        Assert.DoesNotContain("?? \"#000000\"", code, StringComparison.Ordinal);
        Assert.Contains("themeBrushKey ?? \"TextPrimaryBrush\"", code, StringComparison.Ordinal);
    }

    [Fact]
    public void BuiltInDarkScheme_SelectionContrast_MeetsWcag()
    {
        var dark = ColorScheme.CreateDark();
        var text = dark.DarkColors["TextPrimaryColor"];
        var selected = dark.DarkColors["ItemSelectedColor"];
        var hover = dark.DarkColors["ItemHoverColor"];

        // «Светлое на светлом выделение»: текст на выделенной строке обязан оставаться
        // читаемым (WCAG AA для обычного текста — 4.5:1; наведения — вспомогательный фон).
        Assert.True(ColorScheme.ContrastRatio(text, selected) >= 4.5,
            $"Тёмная тема: текст {text} на выделении {selected} должен давать контраст ≥ 4.5:1");
        Assert.True(ColorScheme.ContrastRatio(text, hover) >= 3.0,
            $"Тёмная тема: текст {text} на наведении {hover} должен давать контраст ≥ 3:1");
    }

    [Fact]
    public void BuiltInLightScheme_SelectionContrast_MeetsWcag()
    {
        var light = ColorScheme.CreateLight();
        var text = light.LightColors["TextPrimaryColor"];
        var selected = light.LightColors["ItemSelectedColor"];

        Assert.True(ColorScheme.ContrastRatio(text, selected) >= 4.5,
            $"Светлая тема: текст {text} на выделении {selected} должен давать контраст ≥ 4.5:1");
    }

    [Fact]
    public void GeneratedSchemes_SelectionContrast_MeetsWcag()
    {
        // Генерируемые схемы (issue #271) тоже обязаны сохранять читаемость выделения.
        foreach (var baseHex in new[] { "#3B82F6", "#F59E0B", "#10B981" })
        {
            var scheme = ColorScheme.GenerateFromColor(baseHex);
            Assert.True(ColorScheme.ContrastRatio(scheme.DarkColors["TextPrimaryColor"], scheme.DarkColors["ItemSelectedColor"]) >= 4.5,
                $"Схема {baseHex} (тёмная): контраст выделения ниже 4.5:1");
            Assert.True(ColorScheme.ContrastRatio(scheme.LightColors["TextPrimaryColor"], scheme.LightColors["ItemSelectedColor"]) >= 4.5,
                $"Схема {baseHex} (светлая): контраст выделения ниже 4.5:1");
        }
    }
}