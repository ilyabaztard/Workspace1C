#if WINDOWS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace Workspace1C.Services;

/// <summary>
/// Выбор окна-владельца для модальных окон (issue #41 «Миграция баз»): «Импорт из
/// кластера 1С» и «Миграция баз» открывались из окна «Серверы 1С» с жёстким
/// <c>Owner = MainWindow</c> — пока серверное окно крутит свой модальный цикл, главное
/// окно отключено, и дочернее окно владельца могло оказаться ПОД окном «Серверы 1С»
/// (владелец в WPF всегда держит своё окно над собой, но не поверх остальных окон
/// приложения). Лечение: владельцем становится АКТИВНОЕ видимое окно приложения
/// (в сценарии выше — само окно «Серверы 1С»), запасной вариант — MainWindow.
/// Класс не зависит от UI-платформы только частично: файл собирается в Windows/WPF
/// (#if WINDOWS); Avalonia-аналог той же логики — <c>OwnerWindow()</c> в MainViewModel.
/// </summary>
public static class WindowOwnerResolver
{
    /// <summary>
    /// Выбирает окно-владельца из коллекции: первый видимый И активный, иначе —
    /// <paramref name="mainWindow"/>. Чистая функция: состояние окна передаётся
    /// отдельным предикатом, поэтому юнит-тесты не создают реальных WPF-окон
    /// (см. <see cref="ChooseOwnerCore{TWindow}"/>).
    /// </summary>
    public static Window? ChooseOwner(IEnumerable<Window> windows, Window? mainWindow)
        => ChooseOwnerCore(windows, mainWindow, static w => w.IsVisible && w.IsActive);

    /// <summary>
    /// Чистое ядро выбора (юнит-тестируемое без WPF): возвращает первый элемент
    /// <paramref name="windows"/>, удовлетворяющий <paramref name="isVisibleAndActive"/>,
    /// иначе — <paramref name="mainWindow"/>. Порядок коллекции — это и есть приоритет:
    /// среди нескольких активных видимых окон берётся первое (у «Серверы 1С» поверх
    /// главного окна это и есть актуальное окно, из которого открывают импорт).
    /// Null-коллекция не является штатным входом, но трактуется как пустая —
    /// возвращается запасное окно.
    /// </summary>
    internal static TWindow? ChooseOwnerCore<TWindow>(
        IEnumerable<TWindow> windows,
        TWindow? mainWindow,
        Func<TWindow, bool> isVisibleAndActive)
    {
        if (windows is null)
            return mainWindow;

        foreach (var window in windows)
        {
            if (isVisibleAndActive(window))
                return window;
        }

        return mainWindow;
    }

    /// <summary>
    /// Активное видимое окно приложения (первое в <c>Application.Current.Windows</c>)
    /// с запасным вариантом MainWindow; null, если <c>Application.Current</c> отсутствует
    /// (CLI/тестовый контекст) или в приложении нет ни одного окна. Null-безопасная
    /// обёртка над <see cref="Application.Current"/>.
    /// </summary>
    public static Window? ActiveOrMain()
    {
        var app = Application.Current;
        if (app is null)
            return null;

        return ChooseOwner(app.Windows.OfType<Window>(), app.MainWindow);
    }
}
#endif