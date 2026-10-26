using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Разметочные тесты адаптивной раскладки форм (issue #55, повторная жалоба ArtyoZ:
/// «Все те же формы как обрезались, так и обрезаются»). После фикса 1.0.0.77/1.0.0.66
/// обрезание сохранялось из-за отключённой горизонтальной прокрутки и жёстких ширин
/// комбобоксов/полей во вкладках. Тесты читают исходники из репозитория (как
/// Issue41WindowLayoutTests) и проверяют, что:
///  - контент вкладок имеет горизонтальную прокрутку как fallback (Auto вместо Disabled);
///  - жёсткие Width у полей ввода заменены на MinWidth (Auto+MinWidth для колонок);
///  - длинные подписи переносятся (TextWrapping), длинное имя базы обрезается многоточием;
///  - правки синхронизированы между WPF (*.xaml) и Avalonia (*.Avalonia.cs).
/// </summary>
public sealed class Issue55AdaptiveLayoutTests
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

    private static int Count(string text, string needle) =>
        Regex.Matches(text, Regex.Escape(needle)).Count;

    // ------------------------------------------------------------------
    // «Настройки» (WPF): горизонтальная полоса вкладок - fallback, жёсткие
    // ширины полей -> MinWidth, длинные подписи колонок хоткеев -> Auto+MinWidth.
    // ------------------------------------------------------------------

    [Fact]
    public void SettingsWindow_Wpf_TabContent_HorizontalFallback_And_MinWidth()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.xaml"));

        // Вкладки прокручиваются по вертикали; горизонтальная полоса — fallback:
        // при узком окне контент не обрезается правым краем (issue #55).
        Assert.Contains("HorizontalScrollBarVisibility=\"Auto\"", xaml, StringComparison.Ordinal);
        // Единственная оставшаяся Disabled-полоса — список вкладок слева (таблицы вкладок
        // не должны скроллиться по горизонтали).
        Assert.Equal(1, Count(xaml, "HorizontalScrollBarVisibility=\"Disabled\""));

        // Фиксированные ширины комбобоксов/полей во вкладках заменены MinWidth.
        Assert.Contains("x:Name=\"LanguageComboBox\" MinWidth=\"280\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AfterLaunchActionCombo\" MinWidth=\"230\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SchemeComboBox\" MinWidth=\"240\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"IconSetOverrideComboBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"240\" Height=\"34\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DefaultArchComboBox\" Grid.Column=\"1\" MinWidth=\"200\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DoubleClickActionGlobalCombo\" Height=\"30\" MinWidth=\"200\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MaxLaunchHistoryDepthBox\" Height=\"30\" MinWidth=\"90\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ComDetectTimeoutMsBox\" Height=\"32\" MinWidth=\"120\"", xaml, StringComparison.Ordinal);

        // Жёстких ширин 220/230/240/280/200/120/90/70/60/50 в разметке больше нет
        // (MinWidth/MaxWidth исключаются отрицательным lookbehind).
        var hard = Regex.Matches(xaml, @"(?<![A-Za-z])Width=""(230|240|280|220|200|120|90|70|60|50)""").Count;
        Assert.True(hard == 0, $"В SettingsWindow.xaml остались жёсткие ширины полей ввода: {hard}");

        // Колонки подписей горячих клавиш: Auto + минимальная ширина (было жёсткое 170).
        Assert.True(
            Count(xaml, "<ColumnDefinition Width=\"Auto\" MinWidth=\"150\"/>") >= 20,
            "Колонки подписей горячих клавиш должны быть Auto+MinWidth=150.");
    }

    [Fact]
    public void SettingsWindow_Avalonia_TabContent_HorizontalFallback_And_MinWidth()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.Avalonia.cs"));

        Assert.True(
            Count(code, "HorizontalScrollBarVisibility = ScrollBarVisibility.Auto") >= 8,
            "Контентные ScrollViewer вкладок должны иметь горизонтальную полосу Auto.");

        // Жёсткие ширины полей -> MinWidth (в том числе язык, действие после запуска, ОА).
        Assert.Contains("MinWidth = 280,", code, StringComparison.Ordinal);
        Assert.Contains("afterLaunchBox = new ComboBox { MinWidth = 230, Height = 30 }", code, StringComparison.Ordinal);
        Assert.Contains("MinWidth = 260,", code, StringComparison.Ordinal);
        Assert.Contains("MinWidth = 120,", code, StringComparison.Ordinal);

        // Колонка подписей шрифтов: Auto + MinWidth (было жёсткое 160).
        Assert.Contains("new ColumnDefinition(GridLength.Auto) { MinWidth = 150 }", code, StringComparison.Ordinal);

        var hard = Regex.Matches(code, @"(?<![A-Za-z])Width = (230|240|280|260|250|220|200|120|90|80|70|60|50),").Count;
        Assert.True(hard == 0, $"В SettingsWindow.Avalonia.cs остались жёсткие ширины полей: {hard}");
    }

    [Fact]
    public void SettingsWindow_Avalonia_HotkeyRows_LabelColumnAuto()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.Avalonia.Hotkeys.cs"));
        Assert.Contains("new ColumnDefinition(GridLength.Auto) { MinWidth = 150 }", code, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // «Настройка информационной базы»: горизонтальная полоса вкладок - fallback,
    // комбобокс двойного клика без фиксированной ширины.
    // ------------------------------------------------------------------

    [Fact]
    public void ConnectionSettingsWindow_Wpf_TabsHorizontalFallback_And_ComboMinWidth()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ConnectionSettingsWindow.xaml"));

        Assert.Equal(0, Count(xaml, "HorizontalScrollBarVisibility=\"Disabled\""));
        Assert.True(
            Count(xaml, "VerticalScrollBarVisibility=\"Auto\" HorizontalScrollBarVisibility=\"Auto\" Padding=\"0,0,4,0\"") >= 4,
            "Все вкладки должны иметь горизонтальную полосу Auto (fallback).");
        Assert.Contains("x:Name=\"DoubleClickActionCombo\" MinWidth=\"220\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectionSettingsWindow_Avalonia_TabsHorizontalFallback_And_ComboMinWidth()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ConnectionSettingsWindow.Avalonia.cs"));
        // Общая обёртка вкладок (метод Tab) имеет горизонтальную полосу Auto - покрывает все вкладки.
        Assert.Contains("HorizontalScrollBarVisibility = ScrollBarVisibility.Auto", code, StringComparison.Ordinal);
        // Комбобокс двойного клика без фиксированной ширины.
        Assert.Contains("Content = new ScrollViewer", code, StringComparison.Ordinal);
        Assert.Contains("MinWidth = 220,", code, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // Аудит других окон (по образцу фикса 1.0.0.77): колонки подписей
    // Auto+MinWidth, горизонтальная полоса fallback, перенос длинных подписей.
    // ------------------------------------------------------------------

    [Fact]
    public void ServerEditWindow_LabelColumnAuto_WpfAndAvalonia()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ServerEditWindow.xaml"));
        Assert.Equal(5, Count(xaml, "Width=\"Auto\" MinWidth=\"130\""));

        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ServerEditWindow.Avalonia.cs"));
        Assert.Contains("GridLength.Auto) { MinWidth = 130 }", code, StringComparison.Ordinal);
    }

    [Fact]
    public void LaunchParameterEditWindow_LabelColumnAuto_WpfAndAvalonia()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "LaunchParameterEditWindow.xaml"));
        Assert.Equal(2, Count(xaml, "Width=\"Auto\" MinWidth=\"130\""));

        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "LaunchParameterEditWindow.Avalonia.cs"));
        Assert.Contains("GridLength.Auto) { MinWidth = 130 }", code, StringComparison.Ordinal);
    }

    [Fact]
    public void RacInfobaseCreateWindow_HorizontalFallback_WpfAndAvalonia()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "RacInfobaseCreateWindow.xaml"));
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\" HorizontalScrollBarVisibility=\"Auto\"", xaml, StringComparison.Ordinal);

        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "RacInfobaseCreateWindow.Avalonia.cs"));
        Assert.Contains("GridLength.Auto) { MinWidth = 140 }", code, StringComparison.Ordinal);
    }

    [Fact]
    public void RacInfobasePropertiesWindow_HorizontalFallback_WpfAndAvalonia()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "RacInfobasePropertiesWindow.xaml"));
        Assert.True(
            Count(xaml, "<ScrollViewer VerticalScrollBarVisibility=\"Auto\" HorizontalScrollBarVisibility=\"Auto\">") >= 2,
            "Вкладки свойств ИБ должны иметь горизонтальную полосу Auto.");
        // Длинное имя базы в заголовке не должно обрезаться содержимым.
        Assert.Contains("TextTrimming=\"CharacterEllipsis\"", xaml, StringComparison.Ordinal);

        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "RacInfobasePropertiesWindow.Avalonia.cs"));
        Assert.True(
            Count(code, "HorizontalScrollBarVisibility = ScrollBarVisibility.Auto") >= 2,
            "Обе вкладки Avalonia должны иметь горизонтальную полосу Auto.");
    }

    [Fact]
    public void ScriptScenarioEditWindow_WrapCheckboxLabels_And_HorizontalFallback_WpfAndAvalonia()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ScriptScenarioEditWindow.xaml"));
        // Строка «Интерпретатор + галки»: комбобокс без жёсткой широкой ширины,
        // метки галок переносятся по словам, полоса fallback включена.
        Assert.Contains("MinWidth=\"220\"", xaml, StringComparison.Ordinal);
        Assert.True(Count(xaml, "TextWrapping=\"Wrap\" MaxWidth=\"150\"") >= 2,
            "Метки «Скрывать окно скрипта» и «Уведомлять о запуске» должны переноситься.");
        Assert.Contains("HorizontalScrollBarVisibility=\"Auto\"", xaml, StringComparison.Ordinal);

        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ScriptScenarioEditWindow.Avalonia.cs"));
        Assert.Contains("_shellCombo.MinWidth = 220;", code, StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollBarVisibility = ScrollBarVisibility.Auto", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ScriptPickWindow_Resizable_WithWrappingPreview()
    {
        // Окно «Выбор скрипта»: изменяемое, минимальные размеры есть (глобальный тест),
        // комадная строка превью переносится — не обрезается при сужении окна.
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ScriptPickWindow.xaml"));
        Assert.Contains("MinWidth=\"560\" MinHeight=\"400\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PreviewBox\" IsReadOnly=\"True\" TextWrapping=\"Wrap\"", xaml, StringComparison.Ordinal);
    }
}