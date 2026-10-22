using Workspace1C;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты расчёта минимальной ширины области списка баз (issue #309): сумма ширин
/// ВСЕХ видимых колонок — ведущие кнопки, «Название», колонки значений (включая
/// «Действия») — плюс боковые отступы. Скрытые и звёздные колонки считаются
/// корректно, чтобы горизонтальная полоса доходила до последней колонки.
/// </summary>
public sealed class ListMinWidthCalculatorTests
{
    private static ListMinWidthCalculator.Column C(double width, bool visible = true)
        => new(width, visible);

    [Fact]
    public void Compute_AllVisibleColumns_SumsEveryColumn()
    {
        var leading = new[] { C(24), C(0), C(40), C(26) };
        var values = new[] { C(120), C(160), C(80), C(120), C(200), C(140), C(90), C(130), C(130), C(170) };

        // «Название» звёздная → берётся минимум 220; ведущие: 24+40+26=90;
        // значения: 120+160+80+120+200+140+90+130+130+170=1340; отступы 8*2.
        var total = ListMinWidthCalculator.Compute(nameWidth: 0, nameFallback: 220, leading, padding: 8, values);

        Assert.Equal(220 + 90 + 16 + 1340, total);
    }

    [Fact]
    public void Compute_HiddenColumns_TakeNoSpace()
    {
        // Колонка «Действия» скрыта (ширина 0), «Последняя копия» выключена —
        // места в сумме они не занимают.
        var leading = new[] { C(24), C(0), C(40), C(26) };
        var values = new[]
        {
            C(120), C(160), C(80), C(120), C(200), C(140), C(90), C(130),
            C(130, visible: false), C(0)
        };

        var total = ListMinWidthCalculator.Compute(0, 220, leading, 0, values);

        Assert.Equal(220 + 90 + 1040, total); // без скрытой 130 и нулевой 0
    }

    [Fact]
    public void Compute_FixedNameWidth_UsesItInsteadOfFallback()
    {
        var total = ListMinWidthCalculator.Compute(
            nameWidth: 300, nameFallback: 220,
            Array.Empty<ListMinWidthCalculator.Column>(), 0,
            new[] { C(120) });

        Assert.Equal(300 + 120, total);
    }

    [Fact]
    public void Compute_NoValueColumns_ReturnsNameAndLeadingWithPadding()
    {
        var total = ListMinWidthCalculator.Compute(
            0, 220,
            new[] { C(24), C(40), C(26) },
            8,
            Array.Empty<ListMinWidthCalculator.Column>());

        Assert.Equal(220 + 90 + 16, total);
    }

    [Fact]
    public void SumVisible_IgnoresHiddenAndZeroWidth()
    {
        var columns = new[] { C(50), C(60, visible: false), C(0), C(70) };

        Assert.Equal(120, ListMinWidthCalculator.SumVisible(columns));
    }

    [Fact]
    public void SumVisible_EmptyList_ReturnsZero()
    {
        Assert.Equal(0, ListMinWidthCalculator.SumVisible(Array.Empty<ListMinWidthCalculator.Column>()));
    }

    /// <summary>
    /// Сценарий issue #309: сумма ширин всех колонок больше вьюпорта (узкое окно).
    /// Минимальная ширина должна быть НЕ МЕНЬШЕ фактической суммы — тогда полоса
    /// прокрутки дотягивает до последней (новой) колонки, а не обрезает её.
    /// </summary>
    [Fact]
    public void Compute_ColumnsWiderThanViewport_MinWidthReachesLastColumn()
    {
        // Набор как на скриншоте issue #309: ведущие 24+40+26, фиксированное
        // «Название» 250, десять колонок значений, включая «Действия» и «№ релиза».
        var leading = new[] { C(24), C(40), C(26) };
        var values = new[]
        {
            C(120), C(160), C(170), C(200), C(140), C(90), C(130), C(130), C(160), C(100)
        };

        const double viewport = 982; // ширина окна пользователя со скриншота
        var total = ListMinWidthCalculator.Compute(250, 220, leading, 8, values);

        // 250 + 90 + 16 + 1400 = 1756: минимум строго больше вьюпорта — полоса есть,
        // и равна сумме ВСЕХ видимых колонок — последняя колонка достижима.
        Assert.Equal(250 + (24 + 40 + 26) + 8 * 2 + 1400, total);
        Assert.True(total > viewport, "Минимальная ширина должна превышать вьюпорт — полоса прокрутки нужна.");
        Assert.True(total >= 250 + 90 + 1400, "Полоса должна дотягивать до последней (новой) колонки.");
    }

    /// <summary>
    /// Сценарий-антирегресс issue #255: сумма ширин всех колонок помещается во
    /// вьюпорт — минимальная ширина не должна превышать вьюпорт, иначе появится
    /// ложная горизонтальная полоса при помещающихся колонках.
    /// </summary>
    [Fact]
    public void Compute_ColumnsFitViewport_NoHorizontalScrollbar()
    {
        var leading = new[] { C(24), C(26) };
        var values = new[] { C(120), C(80) };

        const double viewport = 500;
        var total = ListMinWidthCalculator.Compute(150, 220, leading, 8, values);

        // 150 + 50 + 16 + 200 = 416 <= 500: минимум не превышает вьюпорт — полосы нет.
        Assert.Equal(150 + 50 + 16 + 200, total);
        Assert.True(total <= viewport, "Минимальная ширина не должна превышать вьюпорт — полоса прокрутки не нужна.");
    }

    /// <summary>
    /// Fallback имени при нулевой ширине «Названия»: пока колонка звёздная (ширина не
    /// задана перетаскиванием разделителя), в расчёт идёт фиксированный минимум, а не
    /// ноль — иначе сумма занижалась бы и полоса не доезжала бы до последней колонки
    /// (issue #309).
    /// </summary>
    [Fact]
    public void Compute_NameZeroWidth_UsesFallback()
    {
        var leading = new[] { C(24), C(26) };
        var values = new[] { C(120), C(80) };

        var total = ListMinWidthCalculator.Compute(nameWidth: 0, nameFallback: 220, leading, 0, values);

        // 220 (fallback) + 50 + 200 = 470: ноль ширины имени не обнуляет её вклад.
        Assert.Equal(220 + 50 + 200, total);
    }

    /// <summary>
    /// Последняя (одна) колонка в конце списка достижима: сумма видимых колонок,
    /// включая самую правую широкую, попадает в минимум целиком — горизонтальная
    /// полоса обязана дотягивать до неё, а не обрезать (issue #309).
    /// </summary>
    [Fact]
    public void Compute_SingleWideLastColumn_Reachable()
    {
        var leading = new[] { C(24), C(26) };
        // В конце стоит единственная широкая колонка — минимум обязан включать её всю.
        var values = new[] { C(500) };

        const double viewport = 400; // уже: одна широкая колонка не помещается
        var total = ListMinWidthCalculator.Compute(220, 220, leading, 0, values);

        Assert.Equal(220 + 50 + 500, total);
        Assert.True(total > viewport, "Полоса нужна: сумма колонок больше вьюпорта.");
        Assert.True(total >= 220 + 50 + 500,
            "Минимум должен включать правую колонку целиком — последняя колонка достижима.");
    }

    /// <summary>
    /// Регресс issue #309 («пустой хвост» справа от колонок): прокручиваемая область
    /// равна сумме видимых колонок БЕЗ лишнего слагаемого от фактической желаемой
    /// ширины строк (замер контента). Расчётная сумма не зависит от ширины контента —
    /// последняя колонка достижима ровно на границе суммы.
    /// </summary>
    [Fact]
    public void Compute_ColumnSum_NoContentTail()
    {
        var leading = new[] { C(24), C(26) };
        var values = new[] { C(120), C(80) };

        // Сумма колонок 470: минимум равен ей точно, даже если гипотетический контент
        // строк (длинное имя базы) шире — «хвост» в сумму не добавляется (issue #309).
        var total = ListMinWidthCalculator.Compute(220, 220, leading, 0, values);

        Assert.Equal(470, total);
    }

    /// <summary>
    /// Регресс issue #309: сумма колонок считается строго по видимым колонкам —
    /// скрытая колонка (ширина 0) и выключенная (Visible=false) места не занимают,
    /// и «хвост» от них в сумму не попадает.
    /// </summary>
    [Fact]
    public void Compute_HiddenColumn_DoesNotAddToSum()
    {
        var leading = new[] { C(24), C(26) };
        // Последняя колонка скрыта: её ширина 130 не должна попасть в сумму.
        var values = new[]
        {
            C(120), C(80), C(0), C(130, visible: false)
        };

        var total = ListMinWidthCalculator.Compute(220, 220, leading, 0, values);

        Assert.Equal(220 + 50 + 200, total);
    }

    /// <summary>
    /// Сценарий issue #309 (ручное перетаскивание колонок): пользователь раздвинул
    /// колонки шире исходных («взял 4 колонки сразу — подвинул за край видимости») —
    /// сумма ширин растёт, и минимум обязан вырасти вместе с ней, иначе полоса
    /// горизонтальной прокрутки не расширится и раздвинутые колонки останутся
    /// недоступными по прокрутке.
    /// </summary>
    [Fact]
    public void Compute_ColumnsResizedWider_MinWidthGrows()
    {
        var leading = new[] { C(24), C(26) };
        // До перетаскивания: колонки узкие, сумма помещается во вьюпорт.
        var before = new[] { C(120), C(80) };
        var totalBefore = ListMinWidthCalculator.Compute(220, 220, leading, 0, before);

        // После перетаскивания: те же колонки стали заметно шире.
        var after = new[] { C(340), C(260) };
        var totalAfter = ListMinWidthCalculator.Compute(220, 220, leading, 0, after);

        Assert.Equal(220 + 50 + 200, totalBefore);
        Assert.Equal(220 + 50 + 600, totalAfter);
        Assert.True(totalAfter > totalBefore, "После расширения колонок минимум должен вырасти.");

        const double viewport = 500;
        Assert.True(totalAfter > viewport, "Расширенные колонки не помещаются — полоса обязана появиться.");
    }

    /// <summary>
    /// Обратный сценарий issue #309: колонку сузили перетаскиванием — сумма колонок
    /// падает, и минимум не должен «застревать» на прежней широкой величине (иначе
    /// после сужения оставалась бы пустая область справа с лишней полосой).
    /// </summary>
    [Fact]
    public void Compute_ColumnsResizedNarrower_MinWidthShrinks()
    {
        var leading = new[] { C(24), C(26) };
        var wide = new[] { C(340), C(260) };
        var narrow = new[] { C(120), C(80) };

        var totalWide = ListMinWidthCalculator.Compute(220, 220, leading, 0, wide);
        var totalNarrow = ListMinWidthCalculator.Compute(220, 220, leading, 0, narrow);

        Assert.Equal(220 + 50 + 600, totalWide);
        Assert.Equal(220 + 50 + 200, totalNarrow);
        Assert.True(totalNarrow < totalWide, "После сужения колонок минимум должен уменьшаться.");

        const double viewport = 500;
        Assert.True(totalNarrow <= viewport, "Суженные колонки помещаются — ложной полосы нет.");
    }

    /// <summary>
    /// Сценарий issue #21 (жалоба 1): переключение на «Недавние» при помещающихся
    /// колонках. Фактическая сумма заголовка (headerSumNow) отражает текущую ширину
    /// контента и при «залипшем» MinWidth со старого кадра больше вьюпорта, но
    /// расчётная сумма колонок (total) помещается — целевая ширина обязана быть
    /// вьюпортом, а не самоподдерживающейся суммой заголовка.
    /// </summary>
    [Fact]
    public void EffectiveContentWidth_StaleHeaderWiderThanViewport_ColumnsFit_CollapsesToViewport()
    {
        const double total = 900;        // колонки помещаются
        const double viewport = 1000;
        const double headerSumNow = 1001.5; // контент «залип» шире вьюпорта (старый кадр)
        const double scrollable = 1.5;   // полоса есть, но колонки помещаются

        var effective = ListMinWidthCalculator.EffectiveContentWidth(
            total, headerSumNow, viewport, scrollable);

        Assert.Equal(1000, effective);
    }

    /// <summary>
    /// Сценарий issue #21 (жалоба 2): «зависший» скролл — все колонки на экране,
    /// но полоса есть. ScrollableWidth в долях пикселя (0.3), Auto рисует полосу.
    /// Guard схлопывает контент до вьюпорта.
    /// </summary>
    [Fact]
    public void EffectiveContentWidth_SubPixelScrollable_ColumnsFit_CollapsesToViewport()
    {
        const double total = 900;
        const double viewport = 1000;
        const double headerSumNow = 1000.3; // контент «залип» на 0.3px шире вьюпорта
        const double scrollable = 0.3;      // полоса в долях пикселя

        var effective = ListMinWidthCalculator.EffectiveContentWidth(
            total, headerSumNow, viewport, scrollable);

        Assert.Equal(1000, effective);
    }

    /// <summary>
    /// Анти-регресс #255 (issue #21): колонки реально не помещаются (total > viewport + 1),
    /// а контент временно уже вьюпорта (scrollable = 0) — полоса ОБЯЗАНА появиться,
    /// guard «полоса не нужна» не должен схлопывать контент.
    /// </summary>
    [Fact]
    public void EffectiveContentWidth_ColumnsDoNotFit_ScrollableZero_KeepsColumnSum()
    {
        const double total = 1200;       // колонки не помещаются
        const double viewport = 1000;
        const double headerSumNow = 1000; // контент ещё уже вьюпорта (не разложился)
        const double scrollable = 0;      // полосы пока нет

        var effective = ListMinWidthCalculator.EffectiveContentWidth(
            total, headerSumNow, viewport, scrollable);

        Assert.Equal(1200, effective);
    }

    /// <summary>
    /// Сценарий issue #309: колонки не помещаются, и фактическая сумма заголовка
    /// (с округлением) больше расчётной — целевая ширина обязана дотягивать до
    /// последней колонки (максимум из расчётной и фактической суммы).
    /// </summary>
    [Fact]
    public void EffectiveContentWidth_ColumnsDoNotFit_HeaderSumWider_ReachesLastColumn()
    {
        const double total = 1000;
        const double viewport = 999.5;
        const double headerSumNow = 1000.4; // округление последней колонки
        const double scrollable = 0.9;      // полоса меньше 1px, но колонки не помещаются

        var effective = ListMinWidthCalculator.EffectiveContentWidth(
            total, headerSumNow, viewport, scrollable);

        // total > viewport + 1? Нет (1000 <= 1000.5) — guard схлопывает до вьюпорта:
        // полоса меньше 1px бесполезна, а 0.4px обрезки последней колонки невидимы.
        Assert.Equal(999.5, effective);
    }

    /// <summary>
    /// Сценарий issue #309 (полная версия): колонки не помещаются с запасом больше
    /// 1px, и фактическая сумма заголовка больше расчётной — целевая ширина равна
    /// максимуму из них, полоса дотягивает до последней колонки.
    /// </summary>
    [Fact]
    public void EffectiveContentWidth_ColumnsDoNotFitByMargin_HeaderSumWider_KeepsMax()
    {
        const double total = 1000;
        const double viewport = 900;
        const double headerSumNow = 1000.4;
        const double scrollable = 100.4; // полоса реальная

        var effective = ListMinWidthCalculator.EffectiveContentWidth(
            total, headerSumNow, viewport, scrollable);

        Assert.Equal(1000.4, effective);
    }
}