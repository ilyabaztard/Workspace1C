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
}