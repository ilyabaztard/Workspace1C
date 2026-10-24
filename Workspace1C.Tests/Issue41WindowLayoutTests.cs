using System;
using System.IO;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты разметки окон окна «Миграция между кластерами 1С» и «Серверы 1С» (issue #41,
/// комментарии 7OH): кнопки «Импорт»/«Миграция» растягиваются на равные доли ширины
/// (Grid-колонки, а не StackPanel), а ComboBox сервера источника/приёмника связаны
/// двусторонне (выбранный сервер подставляется в поле из VM при открытии окна).
/// </summary>
public sealed class Issue41WindowLayoutTests
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

    [Fact]
    public void ServerMonitorWindow_ImportMigrateButtons_ShareWidthViaGridStarColumns()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ServerMonitorWindow.xaml"));

        // Кнопки «Импорт»/«Миграция» лежат в Grid с двумя звёздными колонками
        // (стековая панель не растягивает Stretch-кнопки — регресс issue #41).
        var idx = xaml.IndexOf("InfobasesImportButton", StringComparison.Ordinal);
        Assert.True(idx >= 0, "Кнопка «Импорт» не найдена в разметке.");
        var region = xaml.Substring(Math.Max(0, idx - 400), 1200);
        Assert.Contains("<Grid", region, StringComparison.Ordinal);
        Assert.DoesNotContain("<StackPanel", region, StringComparison.Ordinal);
        Assert.Contains("Width=\"*\"", region, StringComparison.Ordinal);
    }

    [Fact]
    public void ClusterMigrationWindow_Wpf_ServerCombosBindSelectedItemTwoWay()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ClusterMigrationWindow.xaml"));

        // Привязка SelectedItem серверов двусторонняя (issue #41): сервер, подставленный
        // из окна «Серверы 1С», отображается в поле; выбор вручную пишется в VM.
        Assert.Contains("SelectedItem=\"{Binding SelectedSourceServer, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding SelectedTargetServer, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ClusterMigrationWindow_Avalonia_ServerCombosBindSelectedItemTwoWay()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ClusterMigrationWindow.Avalonia.cs"));

        Assert.Contains("BindingMode.TwoWay", source, StringComparison.Ordinal);
        Assert.Contains("SelectedSourceServer", source, StringComparison.Ordinal);
        Assert.Contains("SelectedTargetServer", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ClusterMigrationWindow_Wpf_HasDbmsAndDbServerFields_AboveCredentials()
    {
        // issue #41, комментарий 7OH: «Тип СУБД» — ComboBox с TwoWay-привязкой Dbms,
        // «Сервер СУБД» — TextBox с DbServer; строка выше полей пользователя/пароля.
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ClusterMigrationWindow.xaml"));

        var dbmsPos = xaml.IndexOf("ClusterMigration.Dbms", StringComparison.Ordinal);
        var dbServerPos = xaml.IndexOf("ClusterMigration.DbServer", StringComparison.Ordinal);
        var dbUserPos = xaml.IndexOf("ClusterMigration.DbUser", StringComparison.Ordinal);
        Assert.True(dbmsPos >= 0, "Поле «Тип СУБД» не найдено в разметке.");
        Assert.True(dbServerPos >= 0, "Поле «Сервер СУБД» не найдено в разметке.");
        Assert.True(dbUserPos > dbServerPos, "Строка «Тип СУБД/Сервер СУБД» должна быть ВЫШЕ полей пользователя/пароля.");

        Assert.Contains("ItemsSource=\"{Binding DbmsOptions}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding Dbms, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding DbServer, UpdateSourceTrigger=PropertyChanged}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ClusterMigrationWindow_Avalonia_HasDbmsAndDbServerBindings_AboveCredentials()
    {
        // issue #41: та же строка «Тип СУБД/Сервер СУБД» над учётными данными СУБД —
        // в axaml разметка (ItemsSource + TwoWay Dbms, DbServer), в коде — привязки box-ов.
        var axaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ClusterMigrationWindow.axaml"));
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ClusterMigrationWindow.Avalonia.cs"));

        var dbmsPos = axaml.IndexOf("ClusterMigration.Dbms", StringComparison.Ordinal);
        var dbServerPos = axaml.IndexOf("ClusterMigration.DbServer", StringComparison.Ordinal);
        var dbUserPos = axaml.IndexOf("ClusterMigration.DbUser", StringComparison.Ordinal);
        Assert.True(dbmsPos >= 0, "Поле «Тип СУБД» не найдено в разметке.");
        Assert.True(dbServerPos >= 0, "Поле «Сервер СУБД» не найдено в разметке.");
        Assert.True(dbUserPos > dbServerPos, "Строка «Тип СУБД/Сервер СУБД» должна быть ВЫШЕ полей пользователя/пароля.");

        Assert.Contains("ItemsSource=\"{Binding DbmsOptions}\"", axaml, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding Dbms, Mode=TwoWay}\"", axaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding DbServer, UpdateSourceTrigger=PropertyChanged}\"", axaml, StringComparison.Ordinal);
        // Код связывает поля с VM (как DbUserBox/DbPasswordBox).
        Assert.Contains("DbmsBox", code, StringComparison.Ordinal);
        Assert.Contains("DbServerBox", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ClusterMigrationWindow_Wpf_NoDbNameField_SummaryAndBulkButtonsAboveOptions()
    {
        // issue #41 (фикс 1.0.0.71, комментарий 7OH): поле «Имя БД» удалено с формы; сводка
        // выделения и кнопки «Выделить все»/«Снять все» стоят сразу под списком (BasesGrid),
        // НАД галочками-опциями GroupByClusterCheck/KeepInSourceCheck.
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ClusterMigrationWindow.xaml"));

        Assert.DoesNotContain("ClusterMigration.DbName", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("DbNameBox", xaml, StringComparison.Ordinal);

        var gridPos = xaml.IndexOf("BasesGrid", StringComparison.Ordinal);
        var summaryPos = xaml.IndexOf("SelectionSummaryText", StringComparison.Ordinal);
        var optionsPos = xaml.IndexOf("GroupByClusterCheck", StringComparison.Ordinal);
        Assert.True(summaryPos >= 0, "Сводка выделения не найдена в разметке.");
        Assert.True(optionsPos >= 0, "Галочки-опции не найдены в разметке.");
        Assert.True(gridPos < summaryPos, "Сводка выделения должна быть ПОД списком баз.");
        Assert.True(summaryPos < optionsPos, "Сводка выделения должна быть ВЫШЕ галочек-опций.");
    }

    [Fact]
    public void ClusterMigrationWindow_Avalonia_NoDbNameField_BulkHostUnderList()
    {
        // issue #41 (фикс 1.0.0.71): поле «Имя БД» удалено из axaml и кода Avalonia; BulkHost
        // (сводка + «Выделить все»/«Снять все») стоит строкой сразу под ListBox, над опциями.
        var axaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ClusterMigrationWindow.axaml"));
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ClusterMigrationWindow.Avalonia.cs"));

        Assert.DoesNotContain("ClusterMigration.DbName", axaml, StringComparison.Ordinal);
        Assert.DoesNotContain("DbNameBox", axaml, StringComparison.Ordinal);
        Assert.DoesNotContain("DbNameBox", code, StringComparison.Ordinal);

        var listPos = axaml.IndexOf("BasesList", StringComparison.Ordinal);
        var bulkPos = axaml.IndexOf("BulkHost", StringComparison.Ordinal);
        var optionsPos = axaml.IndexOf("GroupByClusterCheck", StringComparison.Ordinal);
        Assert.True(listPos >= 0 && bulkPos >= 0 && optionsPos >= 0, "Список/BulkHost/опции не найдены в разметке.");
        Assert.True(listPos < bulkPos, "BulkHost должен быть строкой ПОД списком баз.");
        Assert.True(bulkPos < optionsPos, "BulkHost должен быть ВЫШЕ галочек-опций.");
    }

    [Fact]
    public void ClusterMigrationWindow_Localization_DbNameKeysRemoved()
    {
        // Ключи «Имя БД» и «DbNameEmptyFormat» удалены из обоих словарей (фикс 1.0.0.71) —
        // стража от «мёртвых» строк ru/en (LocalizationCoverageServicesTests сверяет ru/en
        // между собой, здесь — отсутствие удалённых ключей).
        var ru = ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", "ru.json"));
        var en = ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", "en.json"));

        Assert.DoesNotContain("ClusterMigration.DbName", ru, StringComparison.Ordinal);
        Assert.DoesNotContain("ClusterMigration.DbName", en, StringComparison.Ordinal);
        Assert.DoesNotContain("ClusterMigration.Error.DbNameEmptyFormat", ru, StringComparison.Ordinal);
        Assert.DoesNotContain("ClusterMigration.Error.DbNameEmptyFormat", en, StringComparison.Ordinal);
    }
}