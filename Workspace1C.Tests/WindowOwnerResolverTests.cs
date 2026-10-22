#if !LINUX
using System;
using System.Collections.Generic;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты выбора окна-владельца модальных окон (issue #41 «Миграция баз»): «Импорт из
/// кластера 1С» и «Миграция баз», открываемые из окна «Серверы 1С», должны иметь
/// владельцем активное видимое окно приложения (сам монитор серверов), а не жёстко
/// MainWindow — иначе дочернее окно оказывалось ПОД окном «Серверы 1С». Тестируется
/// чистое ядро <see cref="WindowOwnerResolver.ChooseOwnerCore{TWindow}"/>: реальные
/// WPF-окна в юнит-тестах не создаются (не нужны ни STA-поток, ни показ окон),
/// состояния подменяются фейком, а WPF-обёртка <see cref="WindowOwnerResolver.ChooseOwner"/>
/// лишь транслирует <c>IsVisible/IsActive</c> в предикат. Файл обёрнут в #if !LINUX:
/// класс собирается только на Windows (#if WINDOWS).
/// </summary>
public sealed class WindowOwnerResolverTests
{
    /// <summary>Фейковое «окно» для чистого ядра выбора владельца.</summary>
    private sealed record FakeWindow(string Name, bool Visible, bool Active);

    private static FakeWindow Window(string name) => new(name, Visible: false, Active: false);

    private static FakeWindow VisibleWindow(string name) => new(name, Visible: true, Active: false);

    private static FakeWindow ActiveVisibleWindow(string name) => new(name, Visible: true, Active: true);

    [Fact]
    public void ChooseOwnerCore_ActiveVisibleWindow_IsChosen()
    {
        // Среди списка окон выбирается первое активное и видимое: невидимое и
        // видимое-неактивное окна кандидатами не являются, порядок задаёт приоритет.
        var windows = new List<FakeWindow>
        {
            Window("hidden"),
            VisibleWindow("visibleInactive"),
            ActiveVisibleWindow("activeVisible_1"),
            ActiveVisibleWindow("activeVisible_2")
        };

        var result = WindowOwnerResolver.ChooseOwnerCore(
            windows, Window("main"), w => w.Active && w.Visible);

        Assert.Same(windows[2], result);
        Assert.Equal("activeVisible_1", result!.Name);
    }

    [Fact]
    public void ChooseOwnerCore_NoActiveVisibleWindow_FallsBackToMainWindow()
    {
        // Активно-невидимое окно кандидатом не считается: при отсутствии активного
        // видимого владельцем становится запасное окно (MainWindow).
        var windows = new List<FakeWindow>
        {
            Window("hidden"),
            VisibleWindow("visibleInactive"),
            new("activeHidden", Visible: false, Active: true)
        };
        var main = Window("main");

        var result = WindowOwnerResolver.ChooseOwnerCore(
            windows, main, w => w.Active && w.Visible);

        Assert.Same(main, result);
    }

    [Fact]
    public void ChooseOwnerCore_EmptyListAndNullMain_ReturnsNull()
    {
        var result = WindowOwnerResolver.ChooseOwnerCore(
            Array.Empty<FakeWindow>(), null, w => w.Active && w.Visible);

        Assert.Null(result);
    }

    [Fact]
    public void ChooseOwnerCore_EmptyListWithMain_ReturnsMain()
    {
        var main = Window("main");

        var result = WindowOwnerResolver.ChooseOwnerCore(
            Array.Empty<FakeWindow>(), main, w => w.Active && w.Visible);

        Assert.Same(main, result);
    }

    [Fact]
    public void ChooseOwnerCore_NullWindowsCollection_ReturnsMainWithoutThrow()
    {
        // Null-коллекция не является штатным входом (ActiveOrMain передаёт непустую
        // коллекцию Application.Windows), но ядро не должно падать на ней.
        var main = Window("main");

        var result = WindowOwnerResolver.ChooseOwnerCore(
            (IEnumerable<FakeWindow>)null!, main, w => w.Active && w.Visible);

        Assert.Same(main, result);
    }
}
#endif