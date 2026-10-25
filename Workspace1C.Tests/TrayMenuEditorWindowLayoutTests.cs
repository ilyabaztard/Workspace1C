using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Разметочные тесты редактора структуры меню трея (issue #62): WPF-разметка
/// содержит дерево компоновки и кнопки операций, привязанные к
/// <c>TrayMenuEditorViewModel</c>; Avalonia-версия строит те же элементы в коде;
/// в настройках появилась кнопка «Настроить структуру…» с обработчиком;
/// ключи локализации TrayMenu.* симметричны в ru/en.
/// </summary>
public sealed class TrayMenuEditorWindowLayoutTests
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
    public void TrayMenuEditorWindow_Wpf_HasTreeAndButtons_BoundToViewModel()
    {
        // issue #62: окно редактора меню трея (WPF) — дерево верхнего уровня
        // (ItemsSource Roots) и панель операций, привязанная к TrayMenuEditorViewModel.
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "TrayMenuEditorWindow.xaml"));

        Assert.Contains("x:Class=\"Workspace1C.TrayMenuEditorWindow\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Title=\"{loc:Loc TrayMenu.Title}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TreeView", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Roots}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"{Binding IsVisible}\"", xaml, StringComparison.Ordinal);

        // Операции: вверх/вниз/разделитель/сброс — команды VM; диалоги групп —
        // обработчики; стиль блока — выпадающий список VM.
        Assert.Contains("Command=\"{Binding MoveUpCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding MoveDownCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding AddSeparatorCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ResetDefaultCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnAddGroupClick\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnEditGroupClick\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnDeleteClick\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnOk_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsCancel=\"True\"", xaml, StringComparison.Ordinal);

        // Стиль блока «Блок меню»/«Подменю» привязан к VM (issue #62).
        Assert.Contains("ItemsSource=\"{Binding StyleOptions}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding SelectedBlockStyle}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void TrayMenuEditorWindow_Avalonia_BindsTreeWithOperations()
    {
        // issue #62: Avalonia/Linux-версия редактора — то же дерево и операции в коде.
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "TrayMenuEditorWindow.Avalonia.cs"));

        Assert.Contains(": ModalWindowBase", source, StringComparison.Ordinal);
        Assert.Contains("TreeView", source, StringComparison.Ordinal);
        Assert.Contains("ItemsSource = _vm.Roots", source, StringComparison.Ordinal);
        Assert.Contains("_vm.MoveUpCommand", source, StringComparison.Ordinal);
        Assert.Contains("_vm.MoveDownCommand", source, StringComparison.Ordinal);
        Assert.Contains("_vm.AddSeparatorCommand", source, StringComparison.Ordinal);
        Assert.Contains("_vm.ResetDefaultCommand", source, StringComparison.Ordinal);
        Assert.Contains("OnAddGroupClick", source, StringComparison.Ordinal);
        Assert.Contains("OnEditGroupClick", source, StringComparison.Ordinal);
        Assert.Contains("OnDeleteClick", source, StringComparison.Ordinal);
        Assert.Contains("SelectedBlockStyle\", BindingMode.TwoWay", source, StringComparison.Ordinal);
        Assert.Contains("_vm.SaveCommand.Execute(null)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsWindow_HasTrayMenuEditButton_AndHandler()
    {
        // issue #62: группа «Меню в трее» получила кнопку «Настроить структуру…»
        // с обработчиком OnTrayMenuEdit_Click (WPF) / OpenTrayMenuEditor (Avalonia).
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.xaml"));
        Assert.Contains("x:Name=\"TrayMenuEditButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnTrayMenuEdit_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Settings.TrayMenu.EditButton", xaml, StringComparison.Ordinal);

        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.xaml.cs"));
        Assert.Contains("private void OnTrayMenuEdit_Click", code, StringComparison.Ordinal);
        Assert.Contains("AppServices.TryGetService<ITrayMenuLayoutStore>()", code, StringComparison.Ordinal);
        Assert.Contains("new TrayMenuEditorWindow(store)", code, StringComparison.Ordinal);

        var avalonia = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.Avalonia.cs"));
        Assert.Contains("Settings.TrayMenu.EditButton", avalonia, StringComparison.Ordinal);
        Assert.Contains("OpenTrayMenuEditor()", avalonia, StringComparison.Ordinal);
        Assert.Contains("new TrayMenuEditorWindow(store)", avalonia, StringComparison.Ordinal);
        Assert.Contains("win.ShowDialogSync(this)", avalonia, StringComparison.Ordinal);
    }

    [Fact]
    public void EditorSave_RebuildsTrayMenu_OnBothPlatforms()
    {
        // issue #62 (последний комментарий 7OH — иконка не меняется в трее): после
        // «Сохранить» в редакторе меню трея пересобирается БЕЗ ожидания закрытия окна
        // настроек и БЕЗ перезапуска приложения.
        var wpf = ReadProjectFile(Path.Combine("Workspace1C", "Views", "TrayMenuEditorWindow.xaml.cs"));
        Assert.Contains("main.RebuildTrayMenuNow()", wpf, StringComparison.Ordinal);

        var avalonia = ReadProjectFile(Path.Combine("Workspace1C", "Views", "TrayMenuEditorWindow.Avalonia.cs"));
        Assert.Contains("main.RefreshTrayMenuAfterSettings()", avalonia, StringComparison.Ordinal);

        // Avalonia: пересборка после настроек форсируется сбросом отпечатка состава —
        // смена ТОЛЬКО иконки не меняет структуру, и RefreshTrayMenu иначе отбросил бы её.
        var avaloniaTray = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Tray.cs"));
        Assert.Contains("RefreshTrayMenuAfterSettings()", avaloniaTray, StringComparison.Ordinal);
        Assert.Contains("_traySignature = null", avaloniaTray, StringComparison.Ordinal);

        // Отпечаток компоновки учитывает ключ/цвет иконки группы — «ленивая» пересборка
        // реагирует и на смену иконки (TrayMenuBuilder.LayoutSignature).
        var builder = ReadProjectFile(Path.Combine("Workspace1C", "Services", "TrayMenuBuilder.cs"));
        Assert.Contains("item.IconKey", builder, StringComparison.Ordinal);
        Assert.Contains("item.IconColor", builder, StringComparison.Ordinal);
    }

    [Fact]
    public void TrayMenu_LocalizationKeys_SymmetricInRuAndEn()
    {
        // issue #62: наборы ключей TrayMenu.* и Settings.TrayMenu.* ru/en совпадают
        // (значения могут отличаться) — локализация окон редактора не рассинхронизирована.
        var ru = ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", "ru.json"));
        var en = ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", "en.json"));
        var ruKeys = CollectKeys(ru).Where(k => k.StartsWith("TrayMenu.", StringComparison.Ordinal) ||
                                                k.StartsWith("Settings.TrayMenu.", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
        var enKeys = CollectKeys(en).Where(k => k.StartsWith("TrayMenu.", StringComparison.Ordinal) ||
                                                k.StartsWith("Settings.TrayMenu.", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);

        Assert.True(ruKeys.Count >= 25, "Ожидалось не менее 25 ключей TrayMenu.* в ru.json, найдено: " + ruKeys.Count);
        Assert.Equal(ruKeys, enKeys);

        // Все ключи, используемые в разметке/коде новых окон, присутствуют.
        foreach (var key in new[]
                 {
                     "TrayMenu.Title", "TrayMenu.TreeHeader", "TrayMenu.BlockStyleHeader",
                     "TrayMenu.StyleBlock", "TrayMenu.StyleSubmenu", "TrayMenu.MoveHeader",
                     "TrayMenu.MoveUpTooltip", "TrayMenu.MoveDownTooltip", "TrayMenu.AddGroup",
                     "TrayMenu.AddGroupTooltip", "TrayMenu.AddSeparatorTooltip",
                     "TrayMenu.EditGroup", "TrayMenu.EditGroupTooltip", "TrayMenu.Delete",
                     "TrayMenu.DeleteTooltip", "TrayMenu.Reset", "TrayMenu.Save", "TrayMenu.Hint",
                     "TrayMenu.StyleHint", "TrayMenu.GroupsHeader", "TrayMenu.VisibleTooltip",
                     "Settings.TrayMenu.EditButton", "Settings.TrayMenu.Hint",
                 })
        {
            Assert.Contains(key, ruKeys);
        }
    }

    private static System.Collections.Generic.IEnumerable<string> CollectKeys(string json)
    {
        foreach (Match m in Regex.Matches(json, "\"([A-Za-z][\\w.]*)\"\\s*:"))
            yield return m.Groups[1].Value;
    }
}