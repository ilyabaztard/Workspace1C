using System.IO;
using Workspace1C.Models;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #63 (часть A): поля ввода должны визуально отличаться от фона карточки.
/// Глобальная кисть InputBackgroundBrush задана в обеих темах WPF (Light/Dark),
/// в палитрах ColorScheme и в Avalonia (LightTheme.axaml/DarkTheme.axaml +
/// Controls.axaml). Разметочные проверки исходников — по образцу
/// ButtonThemeBindingTests/Issue60SearchIconColorTests.
/// </summary>
public sealed class Issue63InputBackgroundBrushTests
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

    private static string ReadProjectFile(string relativePath)
    {
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
    }

    /// <summary>Фрагмент разметки с начала стиля x:Key до закрывающего тега Style/ControlTheme.</summary>
    private static string StyleBlock(string xaml, string key, string openTag, string closeTag)
    {
        var start = xaml.IndexOf(openTag, System.StringComparison.Ordinal);
        Assert.True(start >= 0, $"Стиль {key} не найден");
        var keyIndex = xaml.IndexOf(key, start, System.StringComparison.Ordinal);
        Assert.True(keyIndex >= 0, $"Ключ {key} не найден в стиле");
        var end = xaml.IndexOf(closeTag, keyIndex, System.StringComparison.Ordinal);
        Assert.True(end >= 0, $"Закрывающий тег {closeTag} для {key} не найден");
        return xaml.Substring(start, end - start + closeTag.Length);
    }

    [Fact]
    public void ControlsAxaml_ModernTextBox_UsesInputBackgroundBrush_NotCardBackground()
    {
        var axaml = ReadProjectFile(Path.Combine("Workspace1C", "Themes", "Controls.axaml"));
        var block = StyleBlock(axaml, "ModernTextBox",
            "<ControlTheme x:Key=\"ModernTextBox\"", "</ControlTheme>");

        Assert.Contains("InputBackgroundBrush", block);
        Assert.DoesNotContain("CardBackgroundBrush", block);
    }

    [Fact]
    public void ControlsAxaml_ModernComboBoxAndModernPasswordBox_UseInputBackgroundBrush()
    {
        var axaml = ReadProjectFile(Path.Combine("Workspace1C", "Themes", "Controls.axaml"));

        var combo = StyleBlock(axaml, "ModernComboBox",
            "<ControlTheme x:Key=\"ModernComboBox\"", "</ControlTheme>");
        Assert.Contains("InputBackgroundBrush", combo);
        Assert.DoesNotContain("CardBackgroundBrush", combo);

        // ModernPasswordBox наследует ModernTextBox (BasedOn) — фон берётся оттуда.
        var pwd = StyleBlock(axaml, "ModernPasswordBox",
            "<ControlTheme x:Key=\"ModernPasswordBox\"", "</ControlTheme>");
        Assert.Contains("BasedOn=\"{StaticResource ModernTextBox}\"", pwd);
    }

    [Fact]
    public void ThemeXaml_Wpf_ModernTextBox_UsesInputBackgroundBrush_LightAndDark()
    {
        foreach (var theme in new[] { "LightTheme.xaml", "DarkTheme.xaml" })
        {
            var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Themes", theme));
            var block = StyleBlock(xaml, "ModernTextBox",
                "<Style x:Key=\"ModernTextBox\"", "</Style>");
            Assert.Contains("DynamicResource InputBackgroundBrush", block);
            Assert.DoesNotContain("DynamicResource CardBackgroundBrush", block);
        }
    }

    [Fact]
    public void ThemeXaml_Wpf_PasswordBox_Style_UsesInputBackgroundBrush()
    {
        foreach (var theme in new[] { "LightTheme.xaml", "DarkTheme.xaml" })
        {
            var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Themes", theme));
            var block = StyleBlock(xaml, "PasswordBox",
                "<Style TargetType=\"PasswordBox\"", "</Style>");
            Assert.Contains("DynamicResource InputBackgroundBrush", block);
            Assert.DoesNotContain("DynamicResource CardBackgroundBrush", block);
        }
    }

    [Fact]
    public void ThemeXaml_Wpf_DeclaresInputBackgroundColorAndBrush_ForBothThemes()
    {
        foreach (var theme in new[] { "LightTheme.xaml", "DarkTheme.xaml" })
        {
            var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Themes", theme));
            Assert.Contains("x:Key=\"InputBackgroundColor\"", xaml);
            Assert.Contains("x:Key=\"InputBackgroundBrush\"", xaml);
        }
    }

    [Fact]
    public void AvaloniaThemes_DeclareInputBackgroundColorAndBrush()
    {
        foreach (var theme in new[] { "LightTheme.axaml", "DarkTheme.axaml" })
        {
            var axaml = ReadProjectFile(Path.Combine("Workspace1C", "Themes", theme));
            Assert.Contains("x:Key=\"InputBackgroundColor\"", axaml);
            Assert.Contains("x:Key=\"InputBackgroundBrush\"", axaml);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ColorScheme_DefaultPalettes_InputBackgroundDiffersFromCardAndContent(bool dark)
    {
        var scheme = dark ? ColorScheme.CreateDark() : ColorScheme.CreateLight();
        var input = scheme.Palette(dark)["InputBackgroundColor"];
        Assert.NotEqual(scheme.Palette(dark)["CardBackgroundColor"], input);
        Assert.NotEqual(scheme.Palette(dark)["ContentBackgroundColor"], input);
        Assert.False(string.IsNullOrWhiteSpace(input));
    }
}