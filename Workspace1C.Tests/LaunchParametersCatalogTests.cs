using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #68 «Конфигуратор параметров запуска»: правка пользовательских параметров
/// в отдельном диалоге, изменение порядка (вверх/вниз) и поиск по справочнику.
/// Чистые функции (фильтр/перемещение/разбор записи) + разметочные проверки окон
/// (по образцу Issue61TitleColorsTests / Issue54EscapeCloseTests).
/// </summary>
public sealed class LaunchParametersCatalogTests
{
    // ---------- FilterReferenceCatalog ----------

    [Fact]
    public void FilterReferenceCatalog_EmptyQuery_ReturnsAll()
    {
        var entries = OneCLaunchArgumentParser.BuildReferenceCatalog(new[] { "/Custom1" });

        var result = OneCLaunchArgumentParser.FilterReferenceCatalog(entries, "");

        Assert.Equal(entries.Count, result.Count);
    }

    [Fact]
    public void FilterReferenceCatalog_NullQuery_ReturnsAll()
    {
        var entries = OneCLaunchArgumentParser.BuildReferenceCatalog();

        var result = OneCLaunchArgumentParser.FilterReferenceCatalog(entries, null);

        Assert.Equal(entries.Count, result.Count);
    }

    [Fact]
    public void FilterReferenceCatalog_Query_MatchesKeyAndDescription_IgnoreCase()
    {
        var entries = OneCLaunchArgumentParser.BuildReferenceCatalog(new[]
        {
            "/MySpecialKey",            // совпадение по ключу
            "/Other\tMySpecialComment"  // совпадение по описанию
        });

        var result = OneCLaunchArgumentParser.FilterReferenceCatalog(entries, "myspecial");

        Assert.Equal(2, result.Count);
        Assert.Contains(result, e => e.Key == "/MySpecialKey");
        Assert.Contains(result, e => e.Key == "/Other");
    }

    [Fact]
    public void FilterReferenceCatalog_NoMatches_ReturnsEmpty()
    {
        var entries = OneCLaunchArgumentParser.BuildReferenceCatalog();

        var result = OneCLaunchArgumentParser.FilterReferenceCatalog(entries, "zzz-no-such");

        Assert.Empty(result);
    }

    // ---------- MoveCustomParameter ----------

    [Fact]
    public void MoveCustomParameter_MovesWithinList_ByKey()
    {
        var list = new List<string> { "/A", "/B\tкомментарий", "/C" };

        Assert.True(OneCLaunchArgumentParser.MoveCustomParameter(list, "/B", -1));
        Assert.Equal(new[] { "/B\tкомментарий", "/A", "/C" }, list);

        // Вниз с последней позиции невозможно — false, список не меняется.
        Assert.False(OneCLaunchArgumentParser.MoveCustomParameter(list, "/C", 1));
        Assert.Equal(new[] { "/B\tкомментарий", "/A", "/C" }, list);
    }

    [Fact]
    public void MoveCustomParameter_FirstUp_LastDown_ReturnsFalse_KeepsList()
    {
        var list = new List<string> { "/A", "/B" };

        Assert.False(OneCLaunchArgumentParser.MoveCustomParameter(list, "/A", -1));
        Assert.False(OneCLaunchArgumentParser.MoveCustomParameter(list, "/B", 1));
        Assert.Equal(new[] { "/A", "/B" }, list);
    }

    [Fact]
    public void MoveCustomParameter_UnknownKey_ReturnsFalse()
    {
        var list = new List<string> { "/A" };

        Assert.False(OneCLaunchArgumentParser.MoveCustomParameter(list, "/Nope", 1));
        Assert.Single(list);
    }

    [Fact]
    public void MoveCustomParameter_MatchesKey_IgnoreCase_WithComment()
    {
        var list = new List<string> { "/A", "/MYKEY\tdesc" };

        Assert.True(OneCLaunchArgumentParser.MoveCustomParameter(list, "/mykey", -1));
        Assert.Equal(new[] { "/MYKEY\tdesc", "/A" }, list);
    }

    // ---------- SplitCustomEntry ----------

    [Fact]
    public void SplitCustomEntry_KeyWithComment_ParsesBoth()
    {
        var (key, comment) = OneCLaunchArgumentParser.SplitCustomEntry("/K\tОписание");

        Assert.Equal("/K", key);
        Assert.Equal("Описание", comment);
    }

    [Fact]
    public void SplitCustomEntry_KeyOnly_NullComment()
    {
        var (key, comment) = OneCLaunchArgumentParser.SplitCustomEntry("/K");

        Assert.Equal("/K", key);
        Assert.Equal(string.Empty, comment);
    }

    // ---------- Разметка окон (WPF/Avalonia) ----------

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

    [Fact]
    public void WpfWindow_HasEditMoveReorderAndSearchControls()
    {
        // Кнопки «Изменить», «Вверх/Вниз», поле поиска и панель добавления
        // присутствуют в WPF-разметке окна «Запустить с выбором параметров».
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "LaunchParametersWindow.xaml"));

        Assert.Contains("OnEditParam_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("OnMoveUp_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("OnMoveDown_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("OnSearchTextChanged", xaml, StringComparison.Ordinal);
        Assert.Contains("LaunchParams.CustomEdit", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaWindow_SupportsCustomParameters()
    {
        // Avalonia-версия окна имеет конструктор с пользовательскими параметрами
        // (правка/порядок/поиск доступны на обеих платформах) и диалог правки.
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "LaunchParametersWindow.Avalonia.cs"));
        var dialog = ReadProjectFile(Path.Combine("Workspace1C", "Views", "LaunchParameterEditWindow.Avalonia.cs"));

        Assert.Contains("IReadOnlyList<string>? customParameters", code, StringComparison.Ordinal);
        Assert.Contains("FilterReferenceCatalog", code, StringComparison.Ordinal);
        Assert.Contains("MoveCustomParameter", code, StringComparison.Ordinal);
        Assert.Contains("LaunchParameterEditWindow", dialog, StringComparison.Ordinal);
    }
}