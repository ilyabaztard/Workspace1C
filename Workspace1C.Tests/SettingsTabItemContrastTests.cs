using System.Text.RegularExpressions;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Единообразие цвета текста выбранного пункта левой панели «Настройки» (issue #26,
/// остаточный дефект после 1.0.0.63): пункты «Резервное копирование» и «Учетные записи»
/// создаются кодом, и их подпись не получала контрастную кисть TreeSelectedTextBrush —
/// текст рисовался дефолтным чёрным. Файловые проверки (по образцу
/// SettingsWindowXamlResourcesTests / UnderlineThemeBindingTests):
/// 1) WPF: стиль SettingsTabItem при IsSelected красит Foreground в TreeSelectedTextBrush;
///    шаблон отдаёт текст через Binding TemplatedParent (TemplateBinding не работает
///    с attached TextElement.Foreground — из-за этого наследование не доходило до
///    заголовков, созданных кодом); код-вкладки привязывают подпись к TabItem.Foreground.
/// 2) Avalonia: тема SettingsTabItem (через базу ConnTabItem) при :selected использует
///    TreeSelectedTextBrush, а все вкладки строятся MainTab/SubTab с этими темами.
/// </summary>
public sealed class SettingsTabItemContrastTests
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

    /// <summary>Возвращает подстроку между двумя маркерами (без XML-комментариев).</summary>
    private static string ExtractBetween(string text, string startMarker, string endMarker)
    {
        text = Regex.Replace(text, "<!--.*?-->", string.Empty, RegexOptions.Singleline);
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Маркер не найден: {startMarker}");
        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Маркер не найден после {startMarker}: {endMarker}");
        return text.Substring(start, end - start);
    }

    // ===================== WPF: стиль SettingsTabItem =====================

    [Fact]
    public void WpfSettingsTabItem_IsSelected_AppliesTreeSelectedTextBrush()
    {
        // Issue #26: у выбранного пункта левой панели фон TreeSelectedBrush (тёмный
        // в тёмной теме) и контрастная подпись TreeSelectedTextBrush, а не чёрная.
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.xaml"));
        var style = ExtractBetween(xaml, "x:Key=\"SettingsTabItem\"", "x:Key=\"SettingsSubTabControl\"");

        Assert.Contains("Trigger Property=\"IsSelected\" Value=\"True\"", style);
        Assert.Contains(
            "<Setter Property=\"Foreground\" Value=\"{DynamicResource TreeSelectedTextBrush}\"/>",
            style);
        // Дополнительная проверка по имени кисти — сеттер не может «потеряться»
        // при переносах строк.
        Assert.Contains("TreeSelectedTextBrush", style);
    }

    [Fact]
    public void WpfSettingsTabItem_Template_ForwardsForegroundViaTemplatedParentBinding()
    {
        // WPF TemplateBinding не работает с attached-свойствами (TextElement.Foreground
        // наследуется абзацу/TextBlock), из-за чего подписи код-вкладок не получали
        // контрастную кисть при выборе. Привязка через TemplatedParent — канонический
        // способ передать Foreground тэгу заголовка в шаблоне.
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.xaml"));
        var style = ExtractBetween(xaml, "x:Key=\"SettingsTabItem\"", "x:Key=\"SettingsSubTabControl\"");

        Assert.Contains(
            "TextElement.Foreground=\"{Binding Foreground, RelativeSource={RelativeSource TemplatedParent}}\"",
            style);
        Assert.DoesNotContain(
            "TextElement.Foreground=\"{TemplateBinding Foreground}\"",
            style);
    }

    [Fact]
    public void WpfSettingsTabs_EveryXamlTab_UsesTabItemThemeStyle()
    {
        // Левая панель «Настроек» в XAML: каждая вертикальная вкладка явно ссылается
        // на SettingsTabItem, подвкладки — на SettingsSubTabItem. Вкладки без этих
        // стилей не получали бы контрастную подпись при выборе.
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.xaml"));
        xaml = Regex.Replace(xaml, "<!--.*?-->", string.Empty, RegexOptions.Singleline);

        var mainTabs = Regex.Matches(xaml, @"<TabItem[^>]*\{StaticResource SettingsTabItem\}");
        Assert.True(mainTabs.Count >= 10,
            $"В XAML окна настроек должно быть не меньше 10 вкладок со стилем SettingsTabItem (найдено: {mainTabs.Count}).");

        var subTabs = Regex.Matches(xaml, @"<TabItem[^>]*\{StaticResource SettingsSubTabItem\}");
        Assert.True(subTabs.Count >= 5,
            $"В XAML окна настроек должно быть не меньше 5 подвкладок со стилем SettingsSubTabItem (найдено: {subTabs.Count}).");
    }

    [Theory]
    [InlineData("SettingsWindow.Profile.cs", "Settings.TabProfile")]
    [InlineData("SettingsWindow.Accounts.cs", "Settings.TabAccounts")]
    public void WpfCodeTabs_ApplyThemeStyleAndBindTitleToTabForeground(string fileName, string titleKey)
    {
        // Вкладки «Резервное копирование» и «Учетные записи» добавляются кодом: они
        // обязаны брать стиль SettingsTabItem (контрастный Foreground при выборе) и
        // привязывать подпись к TabItem.Foreground — иначе текст останется чёрным
        // (issue #26, ArtyoZ, 1.0.0.63).
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", fileName));

        // 1) Стиль вкладки из ресурсов окна.
        Assert.Contains("FindResource(\"SettingsTabItem\")", code);
        // 2) Подпись привязана к Foreground вкладки (как в статичной разметке XAML).
        Assert.Contains("title.SetBinding(TextBlock.ForegroundProperty,", code);
        Assert.Contains("typeof(System.Windows.Controls.TabItem), 1)", code);
        // 3) Заголовок — тот самый ключ локализации.
        Assert.Contains(titleKey, code);
    }

    // ===================== Avalonia: тема и построители вкладок =====================

    [Fact]
    public void AvaloniaConnTabItem_Selected_ForegroundIsTreeSelectedTextBrush()
    {
        // База темы SettingsTabItem: выбранная вкладка красит подпись контрастной
        // кистью (в тёмной теме — светлой), а не нейтральным цветом текста.
        var axaml = ReadProjectFile(Path.Combine("Workspace1C", "Themes", "Controls.axaml"));
        var theme = ExtractBetween(axaml, "x:Key=\"ConnTabItem\"", "x:Key=\"OptionCard\"");

        Assert.Contains("^:selected", theme);
        Assert.Contains("TreeSelectedTextBrush", theme);
        Assert.Contains(
            "<Setter Property=\"Foreground\" Value=\"{DynamicResource TreeSelectedTextBrush}\"/>",
            theme);

        // Фон выбранного пункта — та же тёмная подложка, что в WPF-стиле SettingsTabItem.
        Assert.Contains("TreeSelectedBrush", theme);
    }

    [Fact]
    public void AvaloniaSettingsTabItem_IsBasedOnConnTabItem()
    {
        // SettingsTabItem в Avalonia — тонкая тема-надстройка над ConnTabItem:
        // контрастный Foreground при выборе наследуется от базы.
        var axaml = ReadProjectFile(Path.Combine("Workspace1C", "Themes", "Controls.axaml"));
        var theme = ExtractBetween(axaml, "x:Key=\"SettingsTabItem\"", "x:Key=\"SettingsToggle\"");

        Assert.Contains(
            "BasedOn=\"{StaticResource ConnTabItem}\"",
            theme);
    }

    [Fact]
    public void AvaloniaMainTab_UsesSettingsTabItemTheme_AndInheritsTitleColor()
    {
        // Все вкладки левой панели строятся одним методом MainTab: тема
        // SettingsTabItem применяется, а подпись не перетирает Foreground —
        // контрастная кисть при :selected наследуется подписи из темы.
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.Avalonia.cs"));
        var mainTab = ExtractBetween(
            source,
            "private static TabItem MainTab",
            "private static ToggleButton SettingsSwitch");

        Assert.Contains("tab.Styled(ControlThemes.SettingsTabItem)", mainTab);
        // Подпись без жёсткого цвета — берёт цвет из темы вкладки.
        Assert.Contains("new TextBlock { Text = LocalizationManager.T(titleKey)", mainTab);
        Assert.DoesNotContain(".Foreground =", mainTab);
    }

    [Fact]
    public void AvaloniaSubTab_UsesSettingsSubTabItemTheme()
    {
        // Горизонтальные подвкладки разделов тоже используют тему с контрастной
        // подписью выбранного пункта (issue #26, единый подход обеих платформ).
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.Avalonia.cs"));
        Assert.Contains("tab.Styled(ControlThemes.SettingsSubTabItem)", source);
    }
}
