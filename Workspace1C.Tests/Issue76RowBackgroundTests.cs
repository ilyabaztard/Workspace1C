using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #76 «Цвета фона строк» (7OH, скрин v1.0.0.87): в окне «Миграция между
/// кластерами 1С» в тёмной теме одна строка DataGrid оставалась светло-серой
/// (#E9EBE9 — системная кисть выделения неактивного окна). Причина: локальные
/// DataGrid.CellStyle в окнах перекрывали тематический стиль ячейки без BasedOn —
/// терялись триггеры темы (IsSelected → Transparent), и выбранная строка красилась
/// системными кистями вместо кистей схемы.
/// Стражи: запрет жёстких светлых фонов строк в разметке окон, требование BasedOn
/// у локальных CellStyle, гарантия тематических кистей строк DataGrid окна миграции
/// (WPF и Avalonia).
/// </summary>
public sealed class Issue76RowBackgroundTests
{
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

    /// <summary>Фрагменты XAML, которые намеренно используют светлые цвета (баннер-предупреждение,
    /// плашка избранного) — не строки таблиц, не должны ловиться стражами.</summary>
    private static readonly string[] IntentionalLightBackgrounds =
    {
        "Background=\"#FEF2F2\"", // DeleteInfobaseWindow.xaml — предупреждение о физическом удалении
        "Background=\"#FEF3C7\""  // MainWindow.xaml / MainWindow.Avalonia.Tree.cs — плашка «Избранное»
    };

    [Fact]
    public void WindowXaml_HasNoHardcodedLightRowBackgrounds()
    {
        // Жёсткие светлые фоны (White/#FFF/#FFFFFF/Snow/AliceBlue/WhiteSmoke и светлые hex)
        // запрещены в разметке окон: строки и панели обязаны использовать кисти темы.
        // Исключения — только намеренные цветовые плашки из IntentionalLightBackgrounds.
        foreach (var file in ReadViewFiles("*.xaml"))
        {
            var code = Regex.Replace(File.ReadAllText(file), @"<!--.*?-->", string.Empty, RegexOptions.Singleline);
            foreach (var intentional in IntentionalLightBackgrounds)
                code = code.Replace(intentional, "INTENTIONAL_BADGE", StringComparison.Ordinal);

            foreach (var pattern in new[]
            {
                "Background\\s*=\\s*\"White\"",
                "Background\\s*=\\s*\"#FFF\"",
                "Background\\s*=\\s*\"#FFFFFF\"",
                "Background\\s*=\\s*\"#FFFFFFFF\"",
                "Background\\s*=\\s*\"Snow\"",
                "Background\\s*=\\s*\"AliceBlue\"",
                "Background\\s*=\\s*\"WhiteSmoke\"",
                "RowBackground\\s*=\\s*\"White\"",
                "RowBackground\\s*=\\s*\"#FFF\"",
                "RowBackground\\s*=\\s*\"#FFFFFF\""
            })
            {
                Assert.False(Regex.IsMatch(code, pattern, RegexOptions.IgnoreCase),
                    $"Жёсткий светлый фон строк в {Path.GetFileName(file)}: {pattern}");
            }
        }
    }

    [Fact]
    public void LocalDataGridCellStyles_AlwaysInheritThemeStyle()
    {
        // Локальные DataGrid.CellStyle («вертикаль по центру», рамка, отступы) обязаны
        // наследовать тематический стиль ячейки через BasedOn — иначе теряются триггеры
        // темы, и выбранная/наведённая строка красится системными кистями (issue #76).
        foreach (var file in ReadViewFiles("*.xaml"))
        {
            var code = Regex.Replace(File.ReadAllText(file), @"<!--.*?-->", string.Empty, RegexOptions.Singleline);
            var cellStyleTags = Regex.Matches(code, @"<Style\s+TargetType=""DataGridCell""[^>]*>");
            foreach (Match match in cellStyleTags)
            {
                var tag = match.Value;
                Assert.Contains("BasedOn=\"", tag, StringComparison.Ordinal);
                Assert.Contains("{x:Type DataGridCell}", tag, StringComparison.Ordinal);
                _ = file; // имя файла — для читаемости сбоя
            }
        }
    }

    [Fact]
    public void ClusterMigrationWindow_DataGrid_RowBackgroundsAreThemeBrushes()
    {
        // Окно миграции (скрин issue #76): все три уровня фона строки (обычный,
        // чередующийся, сам DataGrid) — кисти схемы; явные RowStyle-триггеры наведения
        // и выделения — кисти темы; чередование задано явно (AlternationCount=2).
        var xaml = File.ReadAllText(Path.Combine(FindRepoRoot(), "Workspace1C", "Views", "ClusterMigrationWindow.xaml"));

        Assert.Contains("AlternationCount=\"2\"", xaml, StringComparison.Ordinal);
        Assert.Contains("RowBackground=\"{DynamicResource CardBackgroundBrush}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AlternatingRowBackground=\"{DynamicResource CardBackgroundBrush}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Background=\"{DynamicResource CardBackgroundBrush}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<DataGrid.RowStyle>", xaml, StringComparison.Ordinal);
        Assert.Contains("TargetType=\"DataGridRow\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsMouseOver", xaml, StringComparison.Ordinal);
        Assert.Contains("IsSelected", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemHoverBrush", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemSelectedBrush", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ClusterMigrationWindow_Avalonia_ListBoxUsesThemeBackground()
    {
        // Linux: чеклист баз — ListBox; фон задан явной кистью темы, чтобы строки
        // не оставались светлыми в тёмной схеме (issue #76).
        var axaml = File.ReadAllText(Path.Combine(FindRepoRoot(), "Workspace1C", "Views", "ClusterMigrationWindow.axaml"));
        Assert.Contains("x:Name=\"BasesList\"", axaml, StringComparison.Ordinal);
        Assert.Contains("Background=\"{DynamicResource CardBackgroundBrush}\"", axaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Themes_DataGridRowAndCell_UseThemeBrushesNoSystemColors()
    {
        // Стили строки и ячейки DataGrid в обеих темах: фоны и триггеры — только
        // кисти схемы, без системных Highlight/HotTrack (дающих светлые строки).
        foreach (var theme in new[] { "LightTheme.xaml", "DarkTheme.xaml" })
        {
            var text = File.ReadAllText(Path.Combine(FindRepoRoot(), "Workspace1C", "Themes", theme));
            Assert.Contains("<Style TargetType=\"DataGridRow\">", text, StringComparison.Ordinal);
            Assert.Contains("Value=\"{DynamicResource CardBackgroundBrush}\"", text, StringComparison.Ordinal);
            Assert.Contains("ItemHoverBrush", text, StringComparison.Ordinal);
            Assert.Contains("ItemSelectedBrush", text, StringComparison.Ordinal);
            Assert.DoesNotContain("SystemColors.HighlightBrush", text, StringComparison.Ordinal);
            Assert.DoesNotContain("SystemColors.HotTrackBrush", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Background=\"White\"", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ClusterMigrationWindow_Avalonia_CodeHasNoHardcodedLightBackgrounds()
    {
        // Код окна миграции (Linux-версия собирает панели программно): никаких белых/
        // светлых фонов — только строки с комментариями об issue не считаем.
        var code = File.ReadAllText(Path.Combine(FindRepoRoot(), "Workspace1C", "Views", "ClusterMigrationWindow.Avalonia.cs"));
        code = Regex.Replace(code, @"//.*?$", string.Empty, RegexOptions.Multiline);
        Assert.DoesNotContain("Brushes.White", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Color.Parse(\"#FFFFFF\")", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Color.Parse(\"#FFF", code, StringComparison.Ordinal);
    }
}
