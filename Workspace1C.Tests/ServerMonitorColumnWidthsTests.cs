using System;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Юнит-тесты хранилища/расчёта ширины колонок вкладок окна «Серверы 1С» (issue #75):
/// минимальная ширина по содержимому заголовка (худший случай ru/en + стрелка сортировки),
/// сохранение/восстановление по ключам вкладок (изоляция друг от друга), нормализация
/// сохранённых значений (NaN/0/меньше минимума → дефолт/минимум).
/// </summary>
public sealed class ServerMonitorColumnWidthsTests
{
    // ===================== Расчёт минимальной ширины заголовка =====================

    [Fact]
    public void HeaderMinWidth_EmptyHeaders_ReturnsAbsoluteMin()
    {
        // Служебные колонки-глифы («●», «PID» без локализованного заголовка).
        Assert.Equal(ServerMonitorColumnWidthsStore.AbsoluteMinWidth, ServerMonitorColumnWidthsStore.HeaderMinWidth("", ""));
        Assert.Equal(ServerMonitorColumnWidthsStore.AbsoluteMinWidth, ServerMonitorColumnWidthsStore.HeaderMinWidth(null, null));
    }

    [Fact]
    public void HeaderMinWidth_UsesWorstCaseLocaleRuEn()
    {
        // «Информационная база» (19 симв.) против «Infobase» (8): берётся РУССКИЙ вариант.
        var width = ServerMonitorColumnWidthsStore.HeaderMinWidth("Информационная база", "Infobase");
        var expected = Math.Ceiling(19 * ServerMonitorColumnWidthsStore.CharWidthFactor)
                       + ServerMonitorColumnWidthsStore.HeaderReserve;
        Assert.Equal(expected, width);

        // «Application» (11 симв.) против «Приложение» (10): берётся АНГЛИЙСКИЙ вариант.
        var width2 = ServerMonitorColumnWidthsStore.HeaderMinWidth("Приложение", "Application");
        var expected2 = Math.Ceiling(11 * ServerMonitorColumnWidthsStore.CharWidthFactor)
                        + ServerMonitorColumnWidthsStore.HeaderReserve;
        Assert.Equal(expected2, width2);
    }

    [Fact]
    public void HeaderMinWidth_NeverBelowAbsoluteMin()
    {
        var width = ServerMonitorColumnWidthsStore.HeaderMinWidth("ИБ", "IBs");
        Assert.True(width >= ServerMonitorColumnWidthsStore.AbsoluteMinWidth);
        Assert.True(width > 0);
    }

    // ===================== Сохранение/восстановление через AppSettings =====================

    [Fact]
    public void Save_Then_TryLoad_RoundTripsWidths()
    {
        var settings = new AppSettings();
        ServerMonitorColumnWidthsStore.Save(settings, ServerMonitorColumnWidthsStore.SessionsKey,
            new[] { 123.0, 175.0, 250.5 });

        var loaded = ServerMonitorColumnWidthsStore.TryLoad(settings, ServerMonitorColumnWidthsStore.SessionsKey);
        Assert.NotNull(loaded);
        Assert.Equal(new[] { 123.0, 175.0, 250.5 }, loaded);
    }

    [Fact]
    public void TryLoad_MissingTabKey_ReturnsNull()
    {
        var settings = new AppSettings();
        ServerMonitorColumnWidthsStore.Save(settings, ServerMonitorColumnWidthsStore.SessionsKey, new double[] { 100 });

        Assert.Null(ServerMonitorColumnWidthsStore.TryLoad(settings, ServerMonitorColumnWidthsStore.ConnectionsKey));
        Assert.Null(ServerMonitorColumnWidthsStore.TryLoad(null, ServerMonitorColumnWidthsStore.SessionsKey));
    }

    [Fact]
    public void Save_IsolatesTabs_ByTabKey()
    {
        // issue #75: ширина колонок сохраняется по вкладкам независимо — изменение
        // колонок «Сеансы» не влияет на «Соединения» и наоборот.
        var settings = new AppSettings();
        ServerMonitorColumnWidthsStore.Save(settings, ServerMonitorColumnWidthsStore.SessionsKey,
            new[] { 123.0, 175.0 });
        ServerMonitorColumnWidthsStore.Save(settings, ServerMonitorColumnWidthsStore.ConnectionsKey,
            new[] { 200.0 });

        var sessions = ServerMonitorColumnWidthsStore.TryLoad(settings, ServerMonitorColumnWidthsStore.SessionsKey);
        var connections = ServerMonitorColumnWidthsStore.TryLoad(settings, ServerMonitorColumnWidthsStore.ConnectionsKey);
        Assert.Equal(new[] { 123.0, 175.0 }, sessions);
        Assert.Equal(new[] { 200.0 }, connections);
    }

    [Fact]
    public void Save_NormalizesInvalidValues_ToZero()
    {
        // Некорректные значения (NaN/0/отрицательные для «не сохранено») записываются нулём.
        var settings = new AppSettings();
        ServerMonitorColumnWidthsStore.Save(settings, ServerMonitorColumnWidthsStore.ProcessesKey,
            new[] { double.NaN, 0.0, -5.0, 120.0 });

        var loaded = ServerMonitorColumnWidthsStore.TryLoad(settings, ServerMonitorColumnWidthsStore.ProcessesKey);
        Assert.Equal(new[] { 0.0, 0.0, 0.0, 120.0 }, loaded);
    }

    [Fact]
    public void Save_OverwritesExistingEntry_ForSameTabKey()
    {
        var settings = new AppSettings();
        ServerMonitorColumnWidthsStore.Save(settings, ServerMonitorColumnWidthsStore.LocksKey,
            new[] { 100.0, 200.0 });
        ServerMonitorColumnWidthsStore.Save(settings, ServerMonitorColumnWidthsStore.LocksKey,
            new[] { 150.0 });

        var loaded = ServerMonitorColumnWidthsStore.TryLoad(settings, ServerMonitorColumnWidthsStore.LocksKey);
        Assert.Equal(new[] { 150.0 }, loaded);
    }

    [Fact]
    public void NormalizeForLoad_InitializesMissingDictionary()
    {
        // Легаси-конфиг без нового поля: NormalizeForLoad не допускает NullReference
        // при обращении к ServerMonitorColumnWidths (issue #64 / issue #75).
        var settings = new AppSettings { ServerMonitorColumnWidths = null! };
        settings.NormalizeForLoad();
        Assert.NotNull(settings.ServerMonitorColumnWidths);
    }

    // ===================== Применение сохранённых/дефолтных ширин =====================

    [Fact]
    public void ResolveWidths_NoSavedEntry_ReturnsDefaults()
    {
        var defaults = new[] { 110.0, 150.0, 123.0 };
        var mins = new[] { 110.0, 130.0, 123.0 };

        var result = ServerMonitorColumnWidthsStore.ResolveWidths(null, defaults, mins);
        Assert.Equal(new[] { 110.0, 150.0, 123.0 }, result);
    }

    [Fact]
    public void ResolveWidths_SavedBelowMin_IsRaisedToMin()
    {
        // issue #75: заголовок и стрелка сортировки не должны обрезаться даже
        // сохранённой (зауженной) шириной.
        var defaults = new[] { 110.0 };
        var mins = new[] { 123.0 };
        var result = ServerMonitorColumnWidthsStore.ResolveWidths(new[] { 50.0 }, defaults, mins);
        Assert.Equal(new[] { 123.0 }, result);
    }

    [Fact]
    public void ResolveWidths_SavedNan_IsReplacedByDefault()
    {
        var defaults = new[] { 110.0, 150.0 };
        var mins = new[] { 110.0, 130.0 };
        var result = ServerMonitorColumnWidthsStore.ResolveWidths(new[] { double.NaN, 200.0 }, defaults, mins);
        Assert.Equal(new[] { 110.0, 200.0 }, result);
    }

    [Fact]
    public void ResolveWidths_SavedShorterThanColumns_FallsBackForMissing()
    {
        var defaults = new[] { 110.0, 150.0, 123.0 };
        var mins = new[] { 110.0, 130.0, 123.0 };
        var result = ServerMonitorColumnWidthsStore.ResolveWidths(new[] { 300.0 }, defaults, mins);
        Assert.Equal(new[] { 300.0, 150.0, 123.0 }, result);
    }

    [Fact]
    public void AllTabKeys_Present()
    {
        Assert.Contains(ServerMonitorColumnWidthsStore.SessionsKey, ServerMonitorColumnWidthsStore.TabKeys);
        Assert.Contains(ServerMonitorColumnWidthsStore.ConnectionsKey, ServerMonitorColumnWidthsStore.TabKeys);
        Assert.Contains(ServerMonitorColumnWidthsStore.LocksKey, ServerMonitorColumnWidthsStore.TabKeys);
        Assert.Contains(ServerMonitorColumnWidthsStore.ProcessesKey, ServerMonitorColumnWidthsStore.TabKeys);
    }

    // ===================== Перетаскивание границы как в 1С (issue #75, 1.0.0.96) =====================

    [Fact]
    public void MovedLeftColumnWidth_FollowsDelta_AndNeverBelowMin()
    {
        // Обычное перетаскивание: граница двигается — колонки справа едут как блок,
        // левой колонке достаётся изменение Δ ширины (минимум соблюдён).
        Assert.Equal(220.0, ServerMonitorColumnWidthsStore.MovedLeftColumnWidth(200.0, 20.0, 100.0));
        Assert.Equal(180.0, ServerMonitorColumnWidthsStore.MovedLeftColumnWidth(200.0, -20.0, 100.0));
        // Упор в минимум при сжатии: дальше граница не двигается.
        Assert.Equal(100.0, ServerMonitorColumnWidthsStore.MovedLeftColumnWidth(200.0, -250.0, 100.0));
        Assert.Equal(100.0, ServerMonitorColumnWidthsStore.MovedLeftColumnWidth(100.0, 0.0, 100.0));
    }

    [Fact]
    public void ResizedTwoAdjacentWidths_KeepsPairTotal_AndMins()
    {
        // Ctrl: меняется только граница двух смежных колонок, сумма пары постоянна.
        var (left, right) = ServerMonitorColumnWidthsStore.ResizedTwoAdjacentWidths(200.0, 300.0, 50.0, 100.0, 100.0);
        Assert.Equal(250.0, left);
        Assert.Equal(250.0, right);
        Assert.Equal(500.0, left + right);

        // Влево: левая ужимается, правая растёт.
        var (left2, right2) = ServerMonitorColumnWidthsStore.ResizedTwoAdjacentWidths(200.0, 300.0, -80.0, 100.0, 100.0);
        Assert.Equal(120.0, left2);
        Assert.Equal(380.0, right2);
        Assert.Equal(500.0, left2 + right2);
    }

    [Fact]
    public void ResizedTwoAdjacentWidths_StopsAtMins_OfBothColumns()
    {
        // Левая упёрлась в минимум: правая больше не растёт (сумма пары постоянна).
        var (left, right) = ServerMonitorColumnWidthsStore.ResizedTwoAdjacentWidths(150.0, 200.0, -1000.0, 120.0, 90.0);
        Assert.Equal(120.0, left);
        Assert.Equal(230.0, right);
        Assert.Equal(350.0, left + right);

        // Правая упёрлась в минимум: левая больше не растёт.
        var (left2, right2) = ServerMonitorColumnWidthsStore.ResizedTwoAdjacentWidths(150.0, 200.0, 1000.0, 120.0, 90.0);
        Assert.Equal(260.0, left2);
        Assert.Equal(90.0, right2);
        Assert.Equal(350.0, left2 + right2);
    }

    [Fact]
    public void ResizedTwoAdjacentWidths_MinimalPair_NeverBelowMins()
    {
        // Обе колонки уже на минимуме: граница не двигается ни в одну сторону.
        var (left, right) = ServerMonitorColumnWidthsStore.ResizedTwoAdjacentWidths(100.0, 90.0, 25.0, 100.0, 90.0);
        Assert.Equal(100.0, left);
        Assert.Equal(90.0, right);
        var (left2, right2) = ServerMonitorColumnWidthsStore.ResizedTwoAdjacentWidths(100.0, 90.0, -25.0, 100.0, 90.0);
        Assert.Equal(100.0, left2);
        Assert.Equal(90.0, right2);
    }
}
