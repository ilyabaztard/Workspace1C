using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Workspace1C.Themes;

namespace Workspace1C.Converters;

/// <summary>
/// Преобразует ключ иконки (имя Geometry в Icons.xaml) в Geometry для Path.Data.
/// Резолв учитывает активный набор иконок (Задача 5): для Outline ищет ключ с
/// суффиксом ".Outline" (IconsOutline.xaml), при отсутствии — бесшовный fallback
/// на Fluent-геометрию (Icons.xaml), затем на запасную «папку».
/// Результаты кэшируются по эффективному ключу (включая суффикс набора), поэтому
/// переключение набора не даёт устаревших значений — TryFindResource не вызывается
/// на каждую строку списка.
/// </summary>
public class IconKeyToGeometryConverter : IValueConverter, IMultiValueConverter
{
    private static readonly ConcurrentDictionary<string, Geometry> Cache = new(StringComparer.Ordinal);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value as string;
        if (string.IsNullOrWhiteSpace(key))
            key = "IconFolder";

        var set = ThemeFeatureMap.ParseIconSet(
            Application.Current?.Resources[ThemeFeatureMap.IconSetNameKey] as string);
        var effectiveKey = ThemeFeatureMap.IconKeyForSet(key, set);
        return Cache.GetOrAdd(effectiveKey, _ => Resolve(key, set));
    }

    private static Geometry Resolve(string key, IconSet set)
    {
        // Прямой резолв без запасной «папки»: маппинг каталога «Утилит» приоритетнее
        // фолбэк-папки, иначе PackIcon-ключ каталога («Magnify», «Cog») превращался бы
        // в «совсем другую» иконку (папку) в меню трея (issue #62).
        var geometry = ResolveCore(key, set);
        if (geometry is not null)
            return geometry;

        // PackIcon-имя каталога отображается на парный ключ геометрии Icon*
        // (например «Magnify» → «IconSearch»), см. UtilitiesMenuCatalog.WpfIconGeometryKey.
        var mapped = Workspace1C.Services.UtilitiesMenuCatalog.WpfIconGeometryKey(key);
        if (!string.IsNullOrEmpty(mapped) && !string.Equals(mapped, key, StringComparison.Ordinal))
        {
            geometry = ResolveCore(mapped, set);
            if (geometry is not null)
                return geometry;
        }

        // Запасная «папка» — последний кандидат (как раньше).
        return ResolveCore("IconFolder", set) ?? Geometry.Empty;
    }

    /// <summary>Пробует кандидаты резолва ключа, НЕ доходя до запасной «папки»
    /// (её применяет только <see cref="Resolve"/> последним проходом).</summary>
    private static Geometry? ResolveCore(string key, IconSet set)
    {
        foreach (var candidate in ThemeFeatureMap.IconResolutionCandidates(key, set))
        {
            if (string.Equals(candidate, "IconFolder", StringComparison.Ordinal))
                break;
            if (Application.Current?.TryFindResource(candidate) is Geometry geometry)
                return geometry;
        }

        return null;
    }

    /// <summary>
    /// Резолвит геометрию иконки по ключу с учётом активного набора (Задача 5).
    /// Используется там, где иконка получается по ключу напрямую (без привязки),
    /// например в превью выбора иконки группы. Результат кэшируется по эффективному
    /// ключу, как и в <see cref="Convert"/>.
    /// </summary>
    public static Geometry ResolveGeometry(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            key = "IconFolder";

        var set = ThemeFeatureMap.ParseIconSet(
            Application.Current?.Resources[ThemeFeatureMap.IconSetNameKey] as string);
        var effectiveKey = ThemeFeatureMap.IconKeyForSet(key, set);
        return Cache.GetOrAdd(effectiveKey, _ => Resolve(key, set));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    /// <summary>
    /// Вариант для MultiBinding (0.3.12.60): values[0] — ключ иконки, values[1] —
    /// <see cref="IconSetState.Current"/>. Второе значение нужно только как триггер:
    /// при смене набора иконок привязка перевычисляется и иконка перерисовывается
    /// без перезапуска (набор по-прежнему читается из ресурса IconSetName).
    /// </summary>
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
        => Convert(values.Length > 0 ? values[0] : null, targetType, parameter, culture);

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
