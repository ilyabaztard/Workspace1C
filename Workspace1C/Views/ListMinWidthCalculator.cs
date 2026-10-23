using System;
using System.Collections.Generic;

namespace Workspace1C
{
    /// <summary>
    /// Расчёт минимальной ширины области списка баз: сумма ширин ВСЕХ видимых колонок
    /// (ведущие кнопки + «Название» + колонки значений, включая «Действия») плюс
    /// боковые отступы. Общий для WPF и Avalonia, покрыт юнит-тестами (issue #309 —
    /// горизонтальный скролл не доходит до последней колонки).
    /// </summary>
    public static class ListMinWidthCalculator
    {
        /// <summary>Спецификация колонки для расчёта: ширина и видимость.</summary>
        public readonly record struct Column(double Width, bool Visible);

        /// <summary>
        /// Сумма ширин видимых колонок. Скрытые и нулевые колонки места не занимают.
        /// </summary>
        public static double SumVisible(IEnumerable<Column> columns)
        {
            if (columns is null)
                throw new ArgumentNullException(nameof(columns));

            double total = 0;
            foreach (var c in columns)
            {
                if (c.Visible && c.Width > 0)
                    total += c.Width;
            }
            return total;
        }

        /// <summary>
        /// Полная минимальная ширина списка: ведущие колонки, минимум «Названия»
        /// (звёздная колонка считается по <paramref name="nameFallback"/>), колонки
        /// значений с «Действиями» и боковые отступы контента.
        /// </summary>
        public static double Compute(
            double nameWidth,
            double nameFallback,
            IEnumerable<Column> leading,
            double padding,
            IEnumerable<Column> values)
        {
            var name = nameWidth > 0 ? nameWidth : nameFallback;
            return name + SumVisible(leading) + padding * 2 + SumVisible(values);
        }

        /// <summary>
        /// Нужна ли горизонтальная полоса (issue #21, 1.0.0.62): только когда сумма
        /// колонок реально шире вьюпорта. Решение зависит лишь от колонок и ширины
        /// области списка — НЕ от числа строк и не от текущей вкладки, поэтому при
        /// переключении «Все / Избранное / Недавние» полоса не может мигнуть: пока
        /// колонки помещаются, полоса отключена (<c>Disabled</c>) физически.
        /// </summary>
        public static bool NeedsHorizontalScroll(double columnsTotal, double viewport)
            => viewport > 0.5 && columnsTotal > viewport + 0.5;

        /// <summary>
        /// Целевая ширина контента списка (issue #21): максимум из суммы колонок
        /// и вьюпорта, с учётом фактической суммы колонок заголовка (округление)
        /// и guard «полоса не нужна». Общая для WPF и Avalonia, покрыта юнит-тестами.
        /// </summary>
        /// <param name="total">Расчётная сумма видимых колонок (<see cref="Compute"/>).</param>
        /// <param name="headerSumNow">Фактическая сумма <c>ActualWidth</c> колонок заголовка.</param>
        /// <param name="viewportNow">Ширина вьюпорта внешней прокрутки списка.</param>
        /// <param name="scrollableWidth">Текущая прокручиваемая ширина (extent − viewport).</param>
        public static double EffectiveContentWidth(
            double total, double headerSumNow, double viewportNow, double scrollableWidth)
        {
            // Колонки реально не помещаются (расчётная сумма больше вьюпорта): берём
            // максимум из расчётной и фактической суммы заголовка, чтобы полоса
            // гарантированно дотягивала до последней колонки (issue #309). Условие
            // по РАСЧЁТНОЙ сумме, а не по фактической (headerSumNow): фактическая
            // сумма заголовка отражает текущую ширину контента, и при «залипшем»
            // MinWidth (старый кадр после смены вкладки) она самоподдерживалась бы,
            // оставляя полосу при помещающихся колонках (issue #21).
            var effective = Math.Max(
                total > viewportNow ? Math.Max(total, headerSumNow) : total,
                viewportNow);

            // Guard «полоса не нужна» (issue #21): ScrollableWidth в долях пикселя,
            // а Auto рисует полосу при любом положительном значении. Если полоса
            // меньше 1px И расчётная сумма колонок помещается во вьюпорт (с допуском
            // 1px на округление) — схлопываем контент до вьюпорта. Условие по total
            // защищает анти-регресс #255: при колонках, реально не помещающихся
            // (total > viewport + 1), полоса нужна, и схлопывание не выполняется.
            if (scrollableWidth < 1 && total <= viewportNow + 1)
                effective = viewportNow;
            return effective;
        }

    }
}