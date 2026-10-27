using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Разметочные тесты окна «Серверы 1С» (issue #75 «Серверы 1С — колонки»): колонки
/// вкладок «Сеансы/Соединения/Блокировки/Рабочие процессы» на обеих платформах
/// (WPF XAML + Avalonia-код) имеют MinWidth по содержимому заголовка (худший случай
/// ru/en + стрелка сортировки — расчёт <see cref="ServerMonitorColumnWidthsStore"/>),
/// стартовая ширина фиксированных колонок не меньше текста заголовка, а таблицы
/// допускают горизонтальную прокрутку (колонки уходят за границу области).
/// </summary>
public sealed class Issue75ServerMonitorColumnsTests
{
    // ===================== Помощники чтения репозитория/локали =====================

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

    private static IReadOnlyDictionary<string, string> LoadStrings(string languageFile)
    {
        using var doc = JsonDocument.Parse(ReadProjectFile(languageFile));
        var root = doc.RootElement;
        if (!root.TryGetProperty("strings", out var strings))
            return new Dictionary<string, string>();
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var prop in strings.EnumerateObject())
            dict[prop.Name] = prop.Value.GetString() ?? string.Empty;
        return dict;
    }

    /// <summary>Худший случай ru/en для ключа локализации колонки.</summary>
    private static double ExpectedMinWidth(IReadOnlyDictionary<string, string> ru, IReadOnlyDictionary<string, string> en, string key)
    {
        ru.TryGetValue(key, out var ruText);
        en.TryGetValue(key, out var enText);
        return ServerMonitorColumnWidthsStore.HeaderMinWidth(ruText, enText);
    }

    // ===================== Состав колонок вкладок =====================

    /// <summary>Ключи локализации колонок вкладки (null — не локализованный заголовок: PID).</summary>
    private static readonly string?[] SessionsKeys =
    {
        "ServerMonitor.Columns.Session.User",
        "ServerMonitor.Columns.Session.Infobase",
        "ServerMonitor.Columns.Session.Host",
        "ServerMonitor.Columns.Session.App",
        "ServerMonitor.Columns.Session.StartedAt",
        "ServerMonitor.Columns.Session.LastActive",
        "ServerMonitor.Columns.Session.State",
        "ServerMonitor.Columns.Session.Blocked",
        "ServerMonitor.Columns.Session.Memory",
        "ServerMonitor.Columns.Session.Duration",
        "ServerMonitor.Columns.Session.DurationCurrent",
        "ServerMonitor.Columns.Session.Hibernate",
    };

    private static readonly string?[] ConnectionsKeys =
    {
        "ServerMonitor.Columns.Connection.Host",
        "ServerMonitor.Columns.Connection.Port",
        "ServerMonitor.Columns.Connection.Connector",
        "ServerMonitor.Columns.Connection.Established",
        "ServerMonitor.Columns.Connection.LastActive",
        "ServerMonitor.Columns.Connection.Duration",
        "ServerMonitor.Columns.Connection.Descr",
        "ServerMonitor.Columns.Connection.Blocked",
    };

    private static readonly string?[] LocksKeys =
    {
        "ServerMonitor.Columns.Lock.Object",
        "ServerMonitor.Columns.Lock.Waiting",
        "ServerMonitor.Columns.Lock.Blocking",
        "ServerMonitor.Columns.Lock.Session",
        "ServerMonitor.Columns.Lock.Connection",
        "ServerMonitor.Columns.Lock.Transaction",
    };

    private static readonly string?[] ProcessesKeys =
    {
        "ServerMonitor.Columns.Process.Type",
        "ServerMonitor.Columns.Process.Host",
        null, // PID — не локализованный заголовок, минимум абсолютный
        "ServerMonitor.Columns.Process.Port",
        "ServerMonitor.Columns.Process.StartedAt",
        "ServerMonitor.Columns.Process.Memory",
        "ServerMonitor.Columns.Process.Threads",
        "ServerMonitor.Columns.Process.Cpu",
        "ServerMonitor.Columns.Process.Running",
        "ServerMonitor.Columns.Process.Infobases",
    };

    // ===================== WPF XAML =====================

    [Fact]
    public void Wpf_AllFourGrids_AllowHorizontalScroll()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ServerMonitorWindow.xaml"));

        foreach (var gridName in new[] { "SessionsGrid", "ConnectionsGrid", "LocksGrid", "ProcessesGrid" })
        {
            var region = GridRegion(xaml, gridName);
            Assert.True(
                region.Contains("ScrollViewer.HorizontalScrollBarVisibility=\"Auto\"", StringComparison.Ordinal),
                $"Грид {gridName} должен допускать горизонтальную прокрутку (issue #75).");
        }
    }

    [Fact]
    public void Wpf_Columns_HaveMinWidthForLongestHeaderRuEn()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ServerMonitorWindow.xaml"));
        var ru = LoadStrings(Path.Combine("Workspace1C", "Localization", "Languages", "ru.json"));
        var en = LoadStrings(Path.Combine("Workspace1C", "Localization", "Languages", "en.json"));

        AssertColumnsMinWidth(xaml, "SessionsGrid", SessionsKeys, ru, en);
        AssertColumnsMinWidth(xaml, "ConnectionsGrid", ConnectionsKeys, ru, en);
        AssertColumnsMinWidth(xaml, "LocksGrid", LocksKeys, ru, en);
        AssertColumnsMinWidth(xaml, "ProcessesGrid", ProcessesKeys, ru, en);
    }

    [Fact]
    public void Wpf_Columns_StartWidth_NotBelowMinWidth()
    {
        // issue #75: «стартовая ширина» фиксированных колонок не меньше MinWidth —
        // заголовок помещается сразу после открытия окна.
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ServerMonitorWindow.xaml"));

        foreach (var gridName in new[] { "SessionsGrid", "ConnectionsGrid", "LocksGrid", "ProcessesGrid" })
        {
            var region = GridRegion(xaml, gridName);
            foreach (var columnStart in RegionColumns(region))
            {
                var widthText = ReadAttribute(columnStart, "Width");
                var minWidthText = ReadAttribute(columnStart, "MinWidth");
                if (widthText is null || minWidthText is null)
                    continue;
                if (widthText.EndsWith("*", StringComparison.Ordinal))
                    continue; // гибкие колонки ограничены MinWidth, стартовая задаётся звёздами
                Assert.True(ParseNumber(minWidthText, out var minWidth),
                    $"{gridName}: не удалось разобрать MinWidth колонки (issue #75).");
                Assert.True(ParseNumber(widthText, out var startWidth),
                    $"{gridName}: не удалось разобрать Width колонки (issue #75).");
                Assert.True(startWidth >= minWidth,
                    $"{gridName}: стартовая ширина {startWidth} меньше MinWidth={minWidth} (issue #75).");
            }
        }
    }

    private static void AssertColumnsMinWidth(
        string xaml, string gridName, IEnumerable<string?> keys,
        IReadOnlyDictionary<string, string> ru, IReadOnlyDictionary<string, string> en)
    {
        var region = GridRegion(xaml, gridName);
        foreach (var key in keys)
        {
            var marker = key is null ? "Header=\"PID\"" : $"Header=\"{{loc:Loc {key}}}\"";
            Assert.True(region.Contains(marker, StringComparison.Ordinal),
                $"{gridName}: не найдена колонка {marker}.");

            var expected = key is null
                ? ServerMonitorColumnWidthsStore.AbsoluteMinWidth
                : ExpectedMinWidth(ru, en, key);
            var expectedAttribute = $"MinWidth=\"{FormatWidth(expected)}\"";
            var before = region.Substring(0, region.IndexOf(marker, StringComparison.Ordinal));
            var tail = before.Length <= 140 ? before : before.Substring(before.Length - 140);
            Assert.True(tail.Contains(expectedAttribute, StringComparison.Ordinal),
                $"{gridName}/{key}: ожидается MinWidth=\"{FormatWidth(expected)}\" «заголовок + стрелка» (issue #75).");
        }
    }

    private static string GridRegion(string xaml, string gridName)
    {
        var start = xaml.IndexOf($"<DataGrid x:Name=\"{gridName}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Грид {gridName} не найден в разметке.");
        var end = xaml.IndexOf("</DataGrid>", start, StringComparison.Ordinal);
        return xaml.Substring(start, end - start);
    }

    private static IEnumerable<string> RegionColumns(string region)
    {
        var parts = region.Split("<DataGridTextColumn ", StringSplitOptions.RemoveEmptyEntries);
        return parts.Skip(1).Select(p => p.Substring(0, Math.Min(220, p.Length)));
    }

    private static string? ReadAttribute(string columnText, string name)
    {
        var marker = $"{name}=\"";
        var idx = columnText.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0)
            return null;
        var valueStart = idx + marker.Length;
        var valueEnd = columnText.IndexOf('"', valueStart);
        return valueEnd < 0 ? null : columnText.Substring(valueStart, valueEnd - valueStart);
    }

    private static bool ParseNumber(string text, out double value)
        => double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value);

    private static string FormatWidth(double value) => Math.Round(value, 0).ToString(CultureInfo.InvariantCulture);

    // ===================== Avalonia/Linux =====================

    [Fact]
    public void Avalonia_TableHasHorizontalScrollAndRowScrollsTogether()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ServerMonitorWindow.Avalonia.cs"));

        // Заголовок и строки — общий контент горизонтального ScrollViewer (issue #75).
        Assert.True(source.Contains("HorizontalScrollBarVisibility = ScrollBarVisibility.Auto", StringComparison.Ordinal),
            "Avalonia: таблица вкладок должна иметь горизонтальную прокрутку (issue #75).");
        Assert.True(source.Contains("VerticalScrollBarVisibility = ScrollBarVisibility.Disabled", StringComparison.Ordinal),
            "Avalonia: горизонтальный ScrollViewer не должен перехватывать вертикаль у ListBox.");
        Assert.True(source.Contains("Content = dock", StringComparison.Ordinal),
            "Avalonia: заголовок и строки должны прокручиваться вместе.");
        Assert.True(source.Contains("UpdateTableMinWidth", StringComparison.Ordinal),
            "Avalonia: должна быть синхронизация минимальной ширины контента с вьюпортом.");
    }

    [Fact]
    public void Avalonia_ColumnsHaveMinWidthInHeaderGrid()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ServerMonitorWindow.Avalonia.cs"));

        // MinWidth задаётся колонкам и шапки, и строк; при перетаскивании колонка
        // не сжимается ниже минимума (issue #75).
        Assert.True(source.Contains("new ColumnDefinition(widths[i]) { MinWidth = columns[i].MinWidth }", StringComparison.Ordinal),
            "Avalonia: MinWidth должен задаваться колонкам шапки.");
        Assert.True(source.Contains("rowGrid.ColumnDefinitions[i].MinWidth = columns[i].MinWidth", StringComparison.Ordinal),
            "Avalonia: MinWidth должен задаваться колонкам строк.");
        Assert.True(source.Contains("Math.Max(minWidth, startWidth + e.GetPosition(headerGrid).X - startX)", StringComparison.Ordinal),
            "Avalonia: перетаскивание не должно сжимать колонку ниже минимума.");
    }

    [Fact]
    public void Avalonia_ColumnMinWidths_MatchLongestHeaderRuEn()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ServerMonitorWindow.Avalonia.cs"));
        var ru = LoadStrings(Path.Combine("Workspace1C", "Localization", "Languages", "ru.json"));
        var en = LoadStrings(Path.Combine("Workspace1C", "Localization", "Languages", "en.json"));

        AssertColumnSpecs(source, SessionsKeys, ru, en);
        AssertColumnSpecs(source, ConnectionsKeys, ru, en);
        AssertColumnSpecs(source, LocksKeys, ru, en);
        AssertColumnSpecs(source, ProcessesKeys, ru, en);
    }

    private static void AssertColumnSpecs(
        string source, IEnumerable<string?> keys,
        IReadOnlyDictionary<string, string> ru, IReadOnlyDictionary<string, string> en)
    {
        foreach (var key in keys)
        {
            var expected = key is null
                ? ServerMonitorColumnWidthsStore.AbsoluteMinWidth
                : ExpectedMinWidth(ru, en, key);

            // Ключ локализации в исходнике колонки.
            var marker = key is null ? "\"Pid\", \"Pid\", Px(90), 60)" : $"LocalizationManager.T(\"{key}\")";
            var idx = source.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(idx >= 0, $"Колонка {key ?? "PID"} не найдена в Avalonia-коде.");
            var lineStart = source.LastIndexOf('\n', idx) + 1;
            var lineEnd = source.IndexOf('\n', idx);
            var line = source.Substring(lineStart, lineEnd - lineStart).TrimEnd();

            var expectedSuffix = $", {FormatWidth(expected)})";
            Assert.True(line.Contains(expectedSuffix, StringComparison.Ordinal),
                $"{key ?? "PID"}: строка колонки должна содержать {expectedSuffix}, фактически: {line} (issue #75).");
        }
    }
}
