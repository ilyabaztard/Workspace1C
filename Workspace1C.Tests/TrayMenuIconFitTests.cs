using System;
using System.IO;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #62 «Правка меню трея» (7OH: «Иконку сохраняет и показывает. Теперь немного
/// другая проблема. Иконка не влазит — справа обрезана. То ли слева отступ, то ли размер
/// иконки чуть меньше устанавливать, то ли ширину колонки больше.»): глиф каталога в ячейке
/// меню трея обязан вписываться в битмап size×size целиком (масштаб + сдвиг к началу пути +
/// центрирование) — как это делает превью редактора (WPF Path + Stretch.Uniform), а не только
/// масштабироваться в мировых координатах геометрии (viewBox ~24×24 со смещением от нуля —
/// из-за этого правый край глифа уходил за границы битмапа и обрезался).
/// </summary>
public sealed class TrayMenuIconFitTests
{
    private const double Eps = 1e-6;

    [Theory]
    // Fluent-глиф внутри viewBox 24×24 со смещением от начала координат (как у «лупы»: x∈[3; 20,5]).
    [InlineData(3, 3, 17.5, 17.5)]
    [InlineData(2, 2, 20, 20)]
    // Прямоугольник во весь viewBox.
    [InlineData(0, 0, 24, 24)]
    [InlineData(0, 0, 24, 20)]
    // Геометрия шире, чем ячейка.
    [InlineData(1.5, 4, 21, 16)]
    public void Fit_MapsBoundsEntirelyInsideCell_Centered(double minX, double minY, double w, double h)
    {
        const double size = 20;
        var fit = TrayMenuIconFitter.Fit(minX, minY, w, h, size);

        var left = minX * fit.Scale + fit.OffsetX;
        var right = (minX + w) * fit.Scale + fit.OffsetX;
        var top = minY * fit.Scale + fit.OffsetY;
        var bottom = (minY + h) * fit.Scale + fit.OffsetY;

        // Ключевое требование: глиф не выходит за ячейку ни в одну сторону
        // (проблема «иконка обрезана справа» была именно выходом правого края).
        Assert.True(left >= -Eps, $"левая граница обрезана: {left}");
        Assert.True(top >= -Eps, $"верхняя граница обрезана: {top}");
        Assert.True(right <= size + Eps, $"правая граница обрезана: {right} (size={size})");
        Assert.True(bottom <= size + Eps, $"нижняя граница обрезана: {bottom} (size={size})");

        // Пропорции сохранены: масштаб — максимально возможный до вписывания.
        var expectedScale = size * TrayMenuIconFitter.DefaultInset / Math.Max(w, h);
        Assert.Equal(expectedScale, fit.Scale, 6);

        // Центрирование: расстояние до левого края = расстояние до правого (left + right = size),
        // то же по вертикали. Превью редактора центрирует глиф так же (Stretch.Uniform).
        Assert.Equal(size, left + right, 4);
        Assert.Equal(size, top + bottom, 4);
    }

    [Fact]
    public void Fit_PreservesAspectRatio()
    {
        var fit = TrayMenuIconFitter.Fit(0, 0, 24, 12, 20);
        Assert.Equal(20 * TrayMenuIconFitter.DefaultInset / 24, fit.Scale, 6);
        // Пропорции глифа сохраняются: ширина/высота до и после вписывания одинаковы.
        Assert.Equal(24.0 / 12.0, 24 * fit.Scale / (12 * fit.Scale), 6);
    }

    [Fact]
    public void Fit_ExplicitInset_ControlsGlyphShare()
    {
        var full = TrayMenuIconFitter.Fit(0, 0, 24, 24, 20, inset: 1.0);
        Assert.Equal(20.0 / 24.0, full.Scale, 6);

        var half = TrayMenuIconFitter.Fit(0, 0, 24, 24, 20, inset: 0.5);
        Assert.Equal(10.0 / 24.0, half.Scale, 6);
    }

    [Fact]
    public void Fit_InvalidInput_ReturnsIdentityWithoutNaN()
    {
        Assert.Equal(new IconFitTransform(1, 0, 0), TrayMenuIconFitter.Fit(0, 0, 0, 0, 20));
        Assert.Equal(new IconFitTransform(1, 0, 0), TrayMenuIconFitter.Fit(0, 0, 24, -1, 20));
        Assert.Equal(new IconFitTransform(1, 0, 0), TrayMenuIconFitter.Fit(0, 0, 24, 24, 0));

        var fit = TrayMenuIconFitter.Fit(0, 0, 24, 24, 20, inset: 0);
        Assert.False(double.IsNaN(fit.Scale) || double.IsInfinity(fit.Scale));
        Assert.False(double.IsNaN(fit.OffsetX) || double.IsInfinity(fit.OffsetX));
        Assert.False(double.IsNaN(fit.OffsetY) || double.IsInfinity(fit.OffsetY));
    }

    [Fact]
    public void WpfTrayAdapter_InsertsIconIntoCellInsteadOfBareScaling()
    {
        // WPF-адаптер обязан вписывать границы геометрии через TrayMenuIconFitter
        // (масштаб+сдвиг+центрирование), а не голым ScaleTransform в мировых координатах —
        // иначе правый край глифа обрезается битмапом (issue #62, «иконка не влазит — справа
        // обрезана»), хотя превью редактора не обрезается.
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Tray.cs"));

        Assert.Contains("TrayMenuIconFitter.Fit(", code);
        Assert.Contains("new TranslateTransform(fit.OffsetX, fit.OffsetY)", code);
        Assert.DoesNotContain("dc.PushTransform(new ScaleTransform(scale, scale))", code);
    }

    [Fact]
    public void AvaloniaTray_MakesNoIconPromises_NativeMenuIsTextOnly()
    {
        // Linux/NativeMenu не рисует цветные иконки пунктов — статусы групп отражаются
        // текстом, а превью редактора (IconHelper: Viewbox + Stretch.Uniform) центрирует
        // глиф. Регрессировать нечего; страж, что адаптер не притворяется иконками.
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Tray.cs"));

        Assert.DoesNotContain("NativeMenuItem(item.Text) { Icon", code);
        Assert.DoesNotContain(".Icon =", code);
    }

    private static string ReadProjectFile(string relativePath)
    {
        var root = FindRepoRoot();
        var fullPath = Path.Combine(root, relativePath);
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
