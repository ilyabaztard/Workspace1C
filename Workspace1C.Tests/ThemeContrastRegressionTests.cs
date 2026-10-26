using Workspace1C.Models;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Регрессионные тесты контрастности (раздел «Цветовое оформление», issue #26 — последний
/// комментарий ArtyoZ от 2026-10-26): на скриншоте окна «Выбор родительской группы» белая
/// подпись кнопки «на акценте» (PrimaryButton, GroupPickerWindow) на тёмно-янтарном акценте
/// пользовательской схемы (~#AF6B1D) давала лишь 4.25:1 — ниже WCAG AA (4.5:1).
///
/// Причина: кисть TextOnAccentBrush бралась из палитры (фиксированный белый
/// TextOnAccentColor), хотя текст лежит на акцентной заливке. Теперь в ThemeManager
/// (WPF и Avalonia) TextOnAccentBrush пересчитывается контрастным к акценту — так же,
/// как AccentTextBrush; для тёмных панелей (статус-бар, меню выбора окна) введена
/// отдельная SidebarTextBrush, контрастная к SidebarColor.
///
/// Ниже проверяется чистая логика выбора цвета (<see cref="ColorScheme.ContrastTextHex"/>)
/// — та же модель, которой пользуются оба ThemeManager, для светлой и тёмной схем.
/// </summary>
public sealed class ThemeContrastRegressionTests
{
    // Тёмно-янтарные акценты со скриншота, где белый текст не читается: кнопка
    // (#AF6B1D — фон кнопки на скриншоте) и шапка окна (#C66E09).
    [Theory]
    [InlineData("#AF6B1D")]
    [InlineData("#C66E09")]
    public void DarkAmberAccent_ContrastTextIsBlackAndMeetsWcag(string accent)
    {
        var text = ColorScheme.ContrastTextHex(accent);

        Assert.Equal("#000000", text);
        Assert.True(ColorScheme.ContrastRatio(text, accent) >= 4.5,
            $"Текст {text} на фоне {accent} должен давать контраст ≥ 4.5:1");
    }

    [Theory]
    [InlineData("#AF6B1D")]
    [InlineData("#C66E09")]
    public void DarkAmberAccent_FixedWhiteTextWasBelowWcag(string accent)
    {
        // Фактическая причина жалобы: палитра несла фиксированный белый TextOnAccentColor,
        // и на таких акцентах белый текст кнопки не дотягивал до WCAG AA (4.5:1).
        Assert.True(ColorScheme.ContrastRatio("#FFFFFF", accent) < 4.5,
            $"Белый текст на фоне {accent} обязан давать контраст < 4.5:1 (это и была жалоба)");
    }

    /// <summary>TextOnAccentBrush: контрастный текст для всех акцентных цветов обеих схем.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TextOnAccent_ContrastMeetsWcag_ForSchemeAccents(bool dark)
    {
        var scheme = dark ? ColorScheme.CreateDark() : ColorScheme.CreateLight();
        foreach (var key in new[] { "AccentColor", "AccentHoverColor", "AccentPressedColor" })
        {
            var accent = scheme.PaletteValue(dark, key);
            var text = ColorScheme.ContrastTextHex(accent);
            Assert.True(ColorScheme.ContrastRatio(text, accent) >= 4.5,
                $"{key}={accent}: текст {text} должен давать контраст ≥ 4.5:1");
        }
    }

    /// <summary>Любой заданный пользователем акцент (включая тёмный янтарь) читается на кнопке.</summary>
    [Theory]
    [InlineData("#FFB300")] // светлый янтарь
    [InlineData("#FDBF00")]
    [InlineData("#2563EB")] // синий
    [InlineData("#16A34A")] // зелёный
    [InlineData("#7C4DFF")] // фиолетовый
    [InlineData("#13522C")] // тёмная зелень (из жалобы 1.0.0.33)
    [InlineData("#0A84FF")]
    [InlineData("#B4530A")] // TreeSelected тёмной схемы (белый текст на нём корректен)
    [InlineData("#B45309")]
    [InlineData("#AF6B1D")] // тёмный янтарь (со скриншота)
    public void TextOnAccent_ContrastTextAlwaysMeetsWcag(string accent)
    {
        var text = ColorScheme.ContrastTextHex(accent);
        Assert.True(ColorScheme.ContrastRatio(text, accent) >= 4.5,
            $"Текст {text} на фоне {accent} должен давать контраст ≥ 4.5:1");
    }

    /// <summary>SidebarTextBrush: текст статус-бара и панелей выбора окна контрастен Sidebar.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SidebarText_ContrastMeetsWcag(bool dark)
    {
        var scheme = dark ? ColorScheme.CreateDark() : ColorScheme.CreateLight();
        var sidebar = scheme.PaletteValue(dark, "SidebarColor");
        var text = ColorScheme.ContrastTextHex(sidebar);
        Assert.True(ColorScheme.ContrastRatio(text, sidebar) >= 4.5,
            $"Текст {text} на фоне Sidebar {sidebar} должен давать контраст ≥ 4.5:1");
    }

    /// <summary>
    /// Обратная совместимость: сохранённая схема с белым TextOnAccentColor не должна ломать
    /// контраст кнопок — текст на акцентной заливке считается от фактического акцента.
    /// </summary>
    [Fact]
    public void SavedWhiteTextOnAccent_DoesNotOverrideContrastedButtonText()
    {
        var scheme = ColorScheme.CreateDark();
        scheme.DarkColors["TextOnAccentColor"] = "#FFFFFF"; // «старая» сохранённая схема
        scheme.DarkColors["AccentColor"] = "#AF6B1D";

        var buttonText = ColorScheme.ContrastTextHex(scheme.PaletteValue(true, "AccentColor"));

        Assert.Equal("#000000", buttonText);
        Assert.True(ColorScheme.ContrastRatio(buttonText, "#AF6B1D") >= 4.5);
    }
}