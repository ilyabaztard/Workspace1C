namespace Workspace1C.Services;

/// <summary>
/// Чистая математика вписывания иконки каталога в квадратную ячейку меню трея
/// (issue #62, 7OH: «Иконка не влазит — справа обрезана»): масштаб и сдвиг,
/// переводящие прямоугольник bounds (мировые координаты геометрии иконки,
/// viewBox ~24×24, например глиф «лупы» занимает x∈[3; 20,5]) ЦЕЛИКОМ внутрь
/// ячейки size×size с сохранением пропорций и центрированием. Тот же эффект,
/// что у превью редакторов (WPF Path + Stretch.Uniform, Avalonia Viewbox).
/// Класс платформенно-нейтрален (нет WPF/Avalonia-типов), чтобы тесты могли
/// проверять его и на Windows, и на Linux.
/// </summary>
public static class TrayMenuIconFitter
{
    /// <summary>
    /// Доля ячейки для глифа по умолчанию: 0.9 — небольшой внутренний отступ.
    /// При размере ячейки 20 глиф занимает ~18 пикселей (как превью редактора 18×18),
    /// не упирается в края битмапа, где терялись бы пиксели сглаживания.
    /// </summary>
    public const double DefaultInset = 0.9;

    /// <summary>
    /// Считает (Scale, OffsetX, OffsetY), такие что отображение точки p →
    /// p*Scale + (OffsetX, OffsetY) переводит прямоугольник
    /// [minX, minX+width]×[minY, minY+height] целиком в квадрат size×size,
    /// по центру, с сохранением пропорций. Insent — доля ячейки под глиф.
    /// Некорректные входные данные (нулевые/отрицательные размеры) дают
    /// тождественное преобразование — без NaN/Infinity.
    /// </summary>
    public static IconFitTransform Fit(
        double minX, double minY, double width, double height,
        double size, double inset = DefaultInset)
    {
        if (size <= 0d || width <= 0d || height <= 0d)
            return new IconFitTransform(1d, 0d, 0d);

        var fitSize = size * Math.Max(inset, 0.001);
        var scale = Math.Min(fitSize / width, fitSize / height);
        var offsetX = (size - width * scale) / 2d - minX * scale;
        var offsetY = (size - height * scale) / 2d - minY * scale;
        return new IconFitTransform(scale, offsetX, offsetY);
    }
}

/// <summary>Масштаб и сдвиг вписывания: точка p → p*Scale + (OffsetX, OffsetY).</summary>
public readonly record struct IconFitTransform(double Scale, double OffsetX, double OffsetY);
