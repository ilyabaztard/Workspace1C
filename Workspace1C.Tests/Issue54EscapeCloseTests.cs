using System;
using System.IO;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #54 «Массовые действия: окно "Сменить платформу" закрывается по Esc» (7OH):
/// окно закрывается по Esc на обеих платформах. На WPF введено общее правило для новых
/// диалоговых форм — базовый класс <see cref="Workspace1C.EscClosableWindow"/> (закрытие
/// по Esc без модификаторов в <c>PreviewKeyDown</c>; инвариант «сначала подсказка, потом
/// окно» даёт глобальный <see cref="Workspace1C.ToolTipCloser"/>); окно «Сменить
/// платформу» переведено на него, кнопка «Отмена» — <c>IsCancel</c>. На Avalonia общее
/// правило уже даёт <see cref="Workspace1C.ModalWindowBase"/>, от которого окно
/// наследуется. Файловые проверки — по образцу EscCloseWindowsTests / Issue60SearchIconColorTests.
/// </summary>
public sealed class Issue54EscapeCloseTests
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

#if !LINUX
    /// <summary>
    /// WPF: окно «Сменить платформу» наследует общий базовый класс диалогов
    /// <see cref="Workspace1C.EscClosableWindow"/> (закрытие по Esc — общее правило).
    /// Тест обёрнут в #if !LINUX: классы собираются только в Windows-конфигурации
    /// (#if WINDOWS), при кросс-сборке -p:BuildLinux=true их нет.
    /// </summary>
    [Fact]
    public void PlatformChangeWindow_Wpf_InheritsEscClosableWindow()
    {
        Assert.Equal(typeof(Workspace1C.EscClosableWindow), typeof(Workspace1C.PlatformChangeWindow).BaseType);
    }
#endif

    /// <summary>
    /// WPF-разметка: кнопка «Отмена» (CancelButton) — резервный штатный путь закрытия
    /// по Esc (IsCancel), согласованно с другими WPF-окнами (см. EscCloseWindowsTests).
    /// </summary>
    [Fact]
    public void PlatformChangeWindow_Wpf_CancelButtonHasIsCancel()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "PlatformChangeWindow.xaml"));

        var idx = xaml.IndexOf("x:Name=\"CancelButton\"", StringComparison.Ordinal);
        Assert.True(idx >= 0, "Кнопка CancelButton не найдена в разметке.");
        var tag = xaml.Substring(idx, xaml.IndexOf(">", idx) - idx);
        Assert.Contains("IsCancel=\"True\"", tag, StringComparison.Ordinal);
    }

    /// <summary>
    /// WPF-разметка: корневой элемент окна — <c>EscClosableWindow</c> (XAML-компилятор
    /// генерирует partial-класс с базой из корневого элемента, поэтому наследование
    /// фиксируется и в разметке, а не только в .xaml.cs).
    /// </summary>
    [Fact]
    public void PlatformChangeWindow_Wpf_RootElementIsEscClosableWindow()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "PlatformChangeWindow.xaml"));

        Assert.Contains("<local:EscClosableWindow", xaml, StringComparison.Ordinal);
        Assert.Contains("</local:EscClosableWindow>", xaml, StringComparison.Ordinal);
    }

    /// <summary>
    /// Avalonia: объявление класса содержит «: ModalWindowBase» — закрытие по Esc
    /// унаследовано от общего правила (ModalWindowBase.OnKeyDown → CloseAsCancel).
    /// </summary>
    [Fact]
    public void PlatformChangeWindow_Avalonia_InheritsModalWindowBase()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "PlatformChangeWindow.Avalonia.cs"));

        Assert.Contains("class PlatformChangeWindow : ModalWindowBase", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// WPF: базовый класс <see cref="Workspace1C.EscClosableWindow"/> подписывает
    /// PreviewKeyDown и закрывает окно по Esc без модификаторов (проверка исходника).
    /// </summary>
    [Fact]
    public void EscClosableWindow_Wpf_SubscribesPreviewKeyDown()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "EscClosableWindow.cs"));

        Assert.Contains("PreviewKeyDown", source, StringComparison.Ordinal);
        Assert.Contains("Key.Escape", source, StringComparison.Ordinal);
        Assert.Contains("ModifierKeys.None", source, StringComparison.Ordinal);
        Assert.Contains("DialogResult = false", source, StringComparison.Ordinal);
    }
}