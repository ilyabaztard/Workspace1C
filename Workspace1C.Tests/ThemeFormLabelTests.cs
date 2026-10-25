using System;
using System.IO;
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
}