using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Workspace1C.Models;
using Workspace1C.Themes;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #26 «Исправить отображение сохраненных акцентных цветов и контрастность
/// текста в тёмной теме» (последние комментарии 7OH от 2026-10-27/28): стражи против
/// возврата дефектов 1.0.0.102:
///  (1) тёмные надписи «1С Стартер»/«По ссылке» на главной (был ButtonTextBrush,
///      чёрный в тёмной теме, на фоне карточки не читается) — теперь TextPrimary*;
///  (2) белые переключатели в тёмной теме (локальная ColumnVisibilitySwitch с жёсткой
///      дорожкой #CBD5E1 и жёсткие дорожки SettingsToggle) — единая кисть темы
///      SettingsToggleTrackBrush (#475569 в тёмной);
///  (3) жёсткие зелёные кнопки малых диалогов (#16A34A) — переведены на единый
///      контракт ModernButton/SecondaryButton (Windows и Linux);
///  (4) «зебра» таблиц — тематическая кисть ZebraRowBrush в тёмной (#334155) и
///      светлой (#F1F5F9) темах, у DataGrid с AlternationCount нет ни одной
///      бессистемной строки.
/// По образцу Issue61DarkPaletteAuditTests / Issue26ButtonUniformityTests.
/// </summary>
public sealed class Issue26ContrastToggleZebraGuardTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ReadProjectFile(string relativePath)
    {
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
    }

    private static string ReadProjectFileIfExists(string relativePath)
    {
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        return File.Exists(fullPath) ? File.ReadAllText(fullPath) : string.Empty;
    }

    private static string StripComments(string code)
    {
        // Комментарии (XML <!--…--> и //, /* … */) стражи не считают: они лишь
        // описывают проблему и на рендеринг не влияют.
        code = Regex.Replace(code, @"<!--.*?-->", string.Empty, RegexOptions.Singleline);
        code = Regex.Replace(code, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        code = Regex.Replace(code, @"^\s*//.*$", string.Empty, RegexOptions.Multiline);
        return code;
    }

    // ===================== (1) Контраст подписей на главной =====================

    [Fact]
    public void DarkTheme_TextPrimaryOnCard_MeetsWcag()
    {
        // Подписи вторичных кнопок панели действий лежат на фоне карточки #1E293B
        // и должны контрастировать по WCAG ≥ 4.5 (жалоба 7OH про «1С Стартер»/«По ссылке»).
        const string text = "#F1F5F9"; // TextPrimaryColor тёмной темы
        const string background = "#1E293B"; // CardBackgroundColor тёмной темы
        Assert.True(ColorScheme.ContrastRatio(text, background) >= 4.5,
            $"Текст {text} на {background} должен давать контраст ≥ 4.5:1");

        // Контрольный замер: именно чёрный ButtonTextBrush и был дефектом.
        Assert.True(ColorScheme.ContrastRatio("#000000", background) < 4.5,
            "Чёрный текст на тёмной карточке обязан давать малый контраст (баг до исправления)");
    }

    [Fact]
    public void MainWindow_SecondaryActionLabels_UseTextPrimaryNotButtonText()
    {
        // WPF-разметка: на кнопках «1С Стартер»/«По ссылке» больше нет чёрной
        // ButtonTextBrush-подписи (только поясняющий комментарий).
        var main = StripComments(ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml")));
        Assert.DoesNotContain("ButtonTextBrush", main, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"{DynamicResource TextPrimaryBrush}\"", main, StringComparison.Ordinal);

        // Avalonia-код: кнопки действия правой панели берут основной цвет темы.
        var tree = StripComments(ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs")));
        Assert.DoesNotContain("\"ButtonTextBrush\"", tree, StringComparison.Ordinal);
        Assert.Contains("CompactIconAndText(iconKey, text, \"TextPrimaryColorBrush\"", tree, StringComparison.Ordinal);
        Assert.Contains("CompactIconAndText(iconKey, \"\", \"TextPrimaryColorBrush\"", tree, StringComparison.Ordinal);
    }

    // ===================== (2) Переключатели не белые в тёмной теме =====================

    [Fact]
    public void DarkPalette_ToggleTrack_IsDarkNotLight()
    {
        var scheme = ColorScheme.CreateDark();
        var track = scheme.PaletteValue(true, "SettingsToggleTrackColor");
        Assert.Equal("#475569", track);
        // Дорожка выключенного тумблера в тёмной теме обязана быть тёмной (не белой/светлой).
        var luminance = ColorScheme.RelativeLuminance(track);
        Assert.True(luminance < 0.35, $"Дорожка переключателя {track} в тёмной теме слишком светлая (L={luminance:F2})");
    }

    [Fact]
    public void LightPalette_ToggleTrack_IsLight()
    {
        var value = ColorScheme.CreateLight().PaletteValue(false, "SettingsToggleTrackColor");
        Assert.Equal("#CBD5E1", value);
    }

    [Fact]
    public void WpfThemes_HaveSettingsToggleTrackBrush()
    {
        foreach (var theme in new[] { "LightTheme.xaml", "DarkTheme.xaml" })
        {
            var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Themes", theme));
            Assert.Contains("SettingsToggleTrackBrush", xaml);
        }
        // В тёмном словаре кисть привязана к тёмному цвету, а не светлому.
        var dark = ReadProjectFile(Path.Combine("Workspace1C", "Themes", "DarkTheme.xaml"));
        Assert.Contains("<Color x:Key=\"SettingsToggleTrackColor\">#475569</Color>", dark);
    }

    [Fact]
    public void SettingsWindow_ColumnVisibilitySwitch_UsesThemedTrack()
    {
        // Локальная «пилюля» видимости колонок с жёсткой светлой дорожкой #CBD5E1
        // давала белые переключатели в тёмной теме (7OH, 2026-10-27 20:25).
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.xaml"));
        Assert.DoesNotContain("Background=\"#CBD5E1\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Background=\"{DynamicResource SettingsToggleTrackBrush}\"", xaml, StringComparison.Ordinal);
    }

    // ===================== (3) Малые диалоги: единый контракт кнопок =====================

    [Fact]
    public void DialogWindows_NoHardcodedGreenButtons()
    {
        // Материал-диалог и окно обновления (WPF) после 1.0.0.82 всё ещё имели
        // жёсткую зелёную кнопку #16A34A с белым текстом — теперь ModernButton.
        foreach (var path in new[]
        {
            Path.Combine("Workspace1C", "Services", "MaterialMessageWindow.xaml"),
            Path.Combine("Workspace1C", "Services", "UpdateAvailableWindow.xaml")
        })
        {
            var xaml = StripComments(ReadProjectFile(path));
            Assert.DoesNotContain("#16A34A", xaml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Foreground=\"White\"", xaml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Style=\"{DynamicResource ModernButton}\"", xaml, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DialogWindowsAvalonia_NoHardcodedGreenButtons()
    {
        // Linux-версии материального сообщения и ручного обновления — тот же контракт.
        foreach (var path in new[]
        {
            Path.Combine("Workspace1C", "Services", "MaterialMessageWindow.Avalonia.cs"),
            Path.Combine("Workspace1C", "Services", "ManualUpdateWindow.Avalonia.cs")
        })
        {
            var code = StripComments(ReadProjectFileIfExists(path));
            if (code.Length == 0)
                continue; // файл отсутствует в некоторых конфигурациях сборки тестов
            Assert.DoesNotContain("#16A34A", code, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Brushes.White", code, StringComparison.Ordinal);
            Assert.Contains("ControlThemes.ModernButton", code, StringComparison.Ordinal);
        }
    }

    // ===================== (4) «Зебра» таблиц тематическая =====================

    [Fact]
    public void DarkPalette_ZebraRow_IsDarkNotWhite()
    {
        var scheme = ColorScheme.CreateDark();
        var zebra = scheme.PaletteValue(true, "ZebraRowColor");
        Assert.Equal("#334155", zebra);
        Assert.True(ColorScheme.RelativeLuminance(zebra) < 0.35,
            $"Зебра тёмной темы {zebra} не должна быть светлой (L={ColorScheme.RelativeLuminance(zebra):F2})");
        // Отличается от фона карточки — чередование строк остаётся различимым.
        Assert.NotEqual("#1E293B", zebra);
    }

    [Fact]
    public void LightPalette_ZebraRow_IsSubtle()
    {
        Assert.Equal("#F1F5F9", ColorScheme.CreateLight().PaletteValue(false, "ZebraRowColor"));
    }

    [Fact]
    public void WpfThemes_HaveZebraRowBrushInDataGridStyle()
    {
        foreach (var theme in new[] { "LightTheme.xaml", "DarkTheme.xaml" })
        {
            var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Themes", theme));
            Assert.Contains("ZebraRowBrush", xaml);
            // Имплицитный стиль DataGrid адресует AlternatingRowBackground на кисть темы.
            Assert.Contains("Setter Property=\"AlternatingRowBackground\" Value=\"{DynamicResource ZebraRowBrush}\"", xaml);
        }
    }

    [Fact]
    public void DatagridTables_WithAlternation_UseThemedZebra()
    {
        // Окна, включавшие чередование строк либо через RowStyle-триггер, либо через
        // AlternationCount без RowStyle, обязаны указывать тематическую ZebraRowBrush
        // (иначе WPF рисует белыми системными строками в тёмной теме).
        foreach (var path in new[]
        {
            Path.Combine("Workspace1C", "Views", "ActualReleasesWindow.xaml"),
            Path.Combine("Workspace1C", "Views", "PlatformUpdateWindow.xaml"),
            Path.Combine("Workspace1C", "Views", "ConfigTypesEditWindow.xaml"),
            Path.Combine("Workspace1C", "Views", "ItsAccountsWindow.xaml"),
            Path.Combine("Workspace1C", "Views", "ConfigTypeEditWindow.xaml"),
            Path.Combine("Workspace1C", "Views", "PortsEditWindow.xaml")
        })
        {
            var xaml = ReadProjectFile(path);
            Assert.Contains("ZebraRowBrush", xaml);
        }

        // Avalonia-зебра (код-построение строк) тоже ссылается на ZebraRowBrush.
        foreach (var path in new[]
        {
            Path.Combine("Workspace1C", "Views", "ActualReleasesWindow.Avalonia.cs"),
            Path.Combine("Workspace1C", "Views", "ConfigTypesEditWindow.Avalonia.cs"),
            Path.Combine("Workspace1C", "Views", "PlatformUpdateWindow.Avalonia.cs")
        })
        {
            var code = ReadProjectFileIfExists(path);
            if (code.Length == 0)
                continue;
            Assert.Contains("TryBrush(\"ZebraRowBrush\")", code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AvaloniaThemes_HaveZebraAndToggleResources()
    {
        foreach (var theme in new[] { "LightTheme.axaml", "DarkTheme.axaml" })
        {
            var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Themes", theme));
            Assert.Contains("SettingsToggleTrackColor", xaml);
            Assert.Contains("SettingsToggleTrackBrush", xaml);
            Assert.Contains("ZebraRowColor", xaml);
            Assert.Contains("ZebraRowBrush", xaml);
        }
    }

    [Fact]
    public void DarkTheme_SyncLabels_AreBoundToFormLabelBrush()
    {
        // Окно «Синхронизация настроек» (Avalonia/Linux): подписи полей были без
        // привязки к теме и оставались чёрными в тёмной теме — «половина надписей
        // белые, половина чёрные» (7OH, 2026-10-28). Теперь Label использует
        // FormLabelBrush как остальные формы.
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsSyncWindow.Avalonia.cs"));
        Assert.Contains("\"FormLabelBrush\"", code, StringComparison.Ordinal);
        Assert.Contains("\"TextSecondaryColorBrush\"", code, StringComparison.Ordinal);
    }

    // ===================== (5) Кнопки окна и стрелки сортировки (issue #26) =====================

    [Theory]
    [InlineData(WindowButtonStyle.Fluent)]
    [InlineData(WindowButtonStyle.Minimal)]
    [InlineData(WindowButtonStyle.Classic)]
    [InlineData(WindowButtonStyle.Material)]
    [InlineData(WindowButtonStyle.WindowsXp)]
    public void DarkTheme_WindowButtonHover_IsSemiTransparentWhite_NotDaylight(WindowButtonStyle style)
    {
        // «подсветка кнопок окна мышкой при неактивном окне - всё ещё белым цветом»:
        // дневные непрозрачные подложки (#FFF3CD/#E2E8F0/#EDE7F6/#D6E4F0) в тёмной теме
        // смотрелись белой заливкой. В тёмной — полупрозрачный белый, как в Win11/VS Code.
        Assert.Equal("#33FFFFFF", ThemeFeatureMap.ButtonMetrics(style, dark: true).HoverHex);
        // Светлая тема сохраняет прежние «дневные» подложки.
        Assert.NotEqual("#33FFFFFF", ThemeFeatureMap.ButtonMetrics(style, dark: false).HoverHex);
    }

    [Fact]
    public void DarkTheme_WindowButtonHover_MacosAndGlass_DoNotChange()
    {
        // «Светофор» macOS и полупрозрачный Glass сохраняют собственные цветные подложки.
        Assert.Equal("#FF5F57", ThemeFeatureMap.ButtonMetrics(WindowButtonStyle.MacOS, dark: true).HoverHex);
        Assert.Equal("#40FFFFFF", ThemeFeatureMap.ButtonMetrics(WindowButtonStyle.Glass, dark: true).HoverHex);
    }

    [Theory]
    [InlineData("LightTheme.xaml")]
    [InlineData("DarkTheme.xaml")]
    public void WpfThemes_DataGridHeader_HasThemedSortGlyph(string themeFile)
    {
        // «СтрЕлки для кого были нарисованы?» — собственный шаблон заголовка DataGrid
        // не рисовал системную стрелку сортировки. Глиф ▲/▼ добавлен и привязан к
        // вторичной кисти темы — виден и в тёмной, и в светлой теме.
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Themes", themeFile));
        Assert.Contains("x:Name=\"SortGlyph\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SortDirection", xaml, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"{DynamicResource TextSecondaryBrush}\"", xaml, StringComparison.Ordinal);
    }
}
