using Workspace1C.Models;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты расширений <see cref="ColorScheme"/> для полноценных тем (Задача 1):
/// акцентный цвет, встроенные схемы «Синяя»/«Зелёная», клонирование
/// и чтение старых JSON-файлов без акцента.
/// </summary>
public sealed class ColorSchemeThemeTests
{
    [Fact]
    public void CreateLight_HasDefaultAccentColor()
    {
        var scheme = ColorScheme.CreateLight();

        Assert.Equal("Светлая", scheme.Name);
        Assert.Equal("#FDBF00", scheme.AccentColor);
        Assert.NotEmpty(scheme.LightColors);
        Assert.NotEmpty(scheme.DarkColors);
    }

    [Fact]
    public void CreateDark_HasDefaultAccentColor()
    {
        var scheme = ColorScheme.CreateDark();

        Assert.Equal("Тёмная", scheme.Name);
        Assert.Equal("#FFB300", scheme.AccentColor);
        Assert.NotEmpty(scheme.LightColors);
        Assert.NotEmpty(scheme.DarkColors);
    }

    [Fact]
    public void CreateBlue_BuildsSchemeWithAccentInBothPalettes()
    {
        var scheme = ColorScheme.CreateBlue();

        Assert.Equal("Синяя", scheme.Name);
        Assert.Equal("#2563EB", scheme.AccentColor);
        Assert.Equal("#2563EB", scheme.LightColors["AccentColor"]);
        Assert.Equal("#2563EB", scheme.DarkColors["AccentColor"]);
        Assert.Equal("#2563EB", scheme.LightColors["SidebarSelectedColor"]);

        // Производные акценты (hover/pressed) — валидные HEX-цвета.
        Assert.Matches("^#[0-9A-Fa-f]{6}$", scheme.LightColors["AccentHoverColor"]);
        Assert.Matches("^#[0-9A-Fa-f]{6}$", scheme.LightColors["AccentPressedColor"]);
        Assert.NotEmpty(scheme.DarkColors);
    }

    [Fact]
    public void CreateGreen_BuildsSchemeWithAccentInBothPalettes()
    {
        var scheme = ColorScheme.CreateGreen();

        Assert.Equal("Зелёная", scheme.Name);
        Assert.Equal("#16A34A", scheme.AccentColor);
        Assert.Equal("#16A34A", scheme.LightColors["AccentColor"]);
        Assert.Equal("#16A34A", scheme.DarkColors["AccentColor"]);
        Assert.NotEmpty(scheme.LightColors);
        Assert.NotEmpty(scheme.DarkColors);
    }

    [Fact]
    public void CreateMaterial_BuildsSchemeWithAccentAndSurfaces()
    {
        var scheme = ColorScheme.CreateMaterial();

        Assert.Equal("Material Design", scheme.Name);
        Assert.Equal("#7C4DFF", scheme.AccentColor);
        Assert.Equal("#7C4DFF", scheme.LightColors["AccentColor"]);
        Assert.Equal("#7C4DFF", scheme.DarkColors["AccentColor"]);
        // Светлые поверхности #FAFAFA/#FFFFFF, тёмные #121212/#1E1E1E (Задача 3).
        Assert.Equal("#FAFAFA", scheme.LightColors["ContentBackgroundColor"]);
        Assert.Equal("#FFFFFF", scheme.LightColors["CardBackgroundColor"]);
        Assert.Equal("#121212", scheme.DarkColors["ContentBackgroundColor"]);
        Assert.Equal("#1E1E1E", scheme.DarkColors["CardBackgroundColor"]);
        Assert.NotEmpty(scheme.LightColors);
        Assert.NotEmpty(scheme.DarkColors);
    }

    [Fact]
    public void CreateMacOS_BuildsSchemeWithAccentAndSurfaces()
    {
        var scheme = ColorScheme.CreateMacOS();

        Assert.Equal("macOS", scheme.Name);
        Assert.Equal("#0A84FF", scheme.AccentColor);
        Assert.Equal("#0A84FF", scheme.LightColors["AccentColor"]);
        Assert.Equal("#0A84FF", scheme.DarkColors["AccentColor"]);
        // Светлый фон #F5F5F7, тёмный #1E1E1E (Задача 3).
        Assert.Equal("#F5F5F7", scheme.LightColors["ContentBackgroundColor"]);
        Assert.Equal("#1E1E1E", scheme.DarkColors["ContentBackgroundColor"]);
        Assert.NotEmpty(scheme.LightColors);
        Assert.NotEmpty(scheme.DarkColors);
    }

    [Fact]
    public void CreateWindowsXp_BuildsSchemeWithAccentAndSurfaces()
    {
        var scheme = ColorScheme.CreateWindowsXp();

        Assert.Equal("Windows XP", scheme.Name);
        Assert.Equal("#3B6EA5", scheme.AccentColor);
        Assert.Equal("#3B6EA5", scheme.LightColors["AccentColor"]);
        Assert.Equal("#3B6EA5", scheme.DarkColors["AccentColor"]);
        // Светло-голубой фон и классические границы (Задача 3).
        Assert.Equal("#DCE9F7", scheme.LightColors["ContentBackgroundColor"]);
        Assert.Equal("#7F9DB9", scheme.LightColors["BorderColor"]);
        Assert.NotEmpty(scheme.LightColors);
        Assert.NotEmpty(scheme.DarkColors);
    }

    [Fact]
    public void CreateGlass_BuildsSchemeWithAccentAndSemiTransparentSurfaces()
    {
        var scheme = ColorScheme.CreateGlass();

        Assert.Equal("Glass", scheme.Name);
        Assert.Equal("#007AFF", scheme.AccentColor);
        Assert.Equal("#007AFF", scheme.LightColors["AccentColor"]);
        Assert.Equal("#007AFF", scheme.DarkColors["AccentColor"]);
        // Светлая полупрозрачная палитра #AARRGGBB (Задача 3): альфу поддерживают
        // ColorConverter WPF и Color.Parse Avalonia.
        Assert.Matches("^#[0-9A-Fa-f]{8}$", scheme.LightColors["ContentBackgroundColor"]);
        Assert.Matches("^#[0-9A-Fa-f]{8}$", scheme.LightColors["CardBackgroundColor"]);
        Assert.NotEmpty(scheme.LightColors);
        Assert.NotEmpty(scheme.DarkColors);
    }

    [Fact]
    public void Clone_CopiesAccentColorAndPalettesIndependently()
    {
        var original = ColorScheme.CreateBlue();
        var clone = original.Clone();

        Assert.Equal(original.AccentColor, clone.AccentColor);
        Assert.Equal(original.LightColors["AccentColor"], clone.LightColors["AccentColor"]);

        // Изменение копии не влияет на оригинал.
        clone.AccentColor = "#FF0000";
        clone.LightColors["AccentColor"] = "#FF0000";
        Assert.NotEqual("#FF0000", original.AccentColor);
        Assert.NotEqual("#FF0000", original.LightColors["AccentColor"]);
    }

    [Fact]
    public void BuiltInSchemes_IncludeFormLabelBrushInBothPalettes()
    {
        // Issue #61: все встроенные схемы несут ключ FormLabelBrush (подпись поля формы)
        // в обеих палитрах — ThemeManager пересоздаёт кисть по палитре для каждой схемы.
        foreach (var scheme in new[]
        {
            ColorScheme.CreateLight(),
            ColorScheme.CreateDark(),
            ColorScheme.CreateBlue(),
            ColorScheme.CreateGreen(),
            ColorScheme.CreateMaterial(),
            ColorScheme.CreateMacOS()
        })
        {
            Assert.True(scheme.LightColors.ContainsKey("FormLabelBrush"),
                $"Схема «{scheme.Name}» не содержит FormLabelBrush в светлой палитре");
            Assert.True(scheme.DarkColors.ContainsKey("FormLabelBrush"),
                $"Схема «{scheme.Name}» не содержит FormLabelBrush в тёмной палитре");
        }
    }

    [Fact]
    public void GenerateFromColor_IncludesFormLabelBrushMatchingTextPrimary()
    {
        var scheme = ColorScheme.GenerateFromColor("#2563EB");

        Assert.Equal(scheme.LightColors["TextPrimaryColor"], scheme.LightColors["FormLabelBrush"]);
        Assert.Equal(scheme.DarkColors["TextPrimaryColor"], scheme.DarkColors["FormLabelBrush"]);
    }

    [Fact]
    public void FromJson_NormalizedScheme_IncludesFormLabelBrushFollowsTextPrimary()
    {
        // Старая пользовательская схема без ключа FormLabelBrush: после Normalize()
        // кисть появляется и повторяет собственный TextPrimaryColor схемы (issue #61).
        const string legacyJson = """
        {
          "Name": "Фирменная",
          "LightColors": {
            "TextPrimaryColor": "#121212",
            "CardBackgroundColor": "#FFFFFF"
          },
          "DarkColors": {
            "TextPrimaryColor": "#EEEEEE",
            "CardBackgroundColor": "#222222"
          }
        }
        """;

        var scheme = ColorScheme.FromJson(legacyJson);
        Assert.NotNull(scheme);
        Assert.Equal("#121212", scheme!.LightColors["FormLabelBrush"]);
        Assert.Equal("#EEEEEE", scheme.DarkColors["FormLabelBrush"]);
    }

    [Fact]
    public void FromJson_ReadsLegacyFileWithoutAccentColor()
    {
        // Старый формат: имя и (частичная) палитра без поля AccentColor.
        const string legacyJson = """
        {
          "Name": "Старая схема",
          "LightColors": {
            "AccentColor": "#112233",
            "ContentBackgroundColor": "#FFFFFF"
          }
        }
        """;

        var scheme = ColorScheme.FromJson(legacyJson);

        Assert.NotNull(scheme);
        Assert.Equal("Старая схема", scheme!.Name);
        // Normalize() заполнил акцент из активной (светлой) палитры.
        Assert.Equal("#112233", scheme.AccentColor);
        // Отсутствующие ключи дополнены значениями по умолчанию.
        Assert.Equal("#FFFFFF", scheme.LightColors["ContentBackgroundColor"]);
        Assert.NotEmpty(scheme.DarkColors);
    }

    [Fact]
    public void ToJson_IncludesAccentColor_AndOmitsNullOne()
    {
        var scheme = ColorScheme.CreateGreen();
        var json = scheme.ToJson();

        Assert.Contains("\"AccentColor\": \"#16A34A\"", json);

        // Пустой акцент не пишется (JsonIgnoreCondition.WhenWritingNull).
        var noAccent = new ColorScheme { Name = "Без акцента" };
        Assert.DoesNotContain("\"AccentColor\"", noAccent.ToJson());
    }
}