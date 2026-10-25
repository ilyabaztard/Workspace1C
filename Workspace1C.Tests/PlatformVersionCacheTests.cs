using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты TTL-кэша поиска каталогов платформ (issue #67, производительность подстановок
/// сценариев): рекурсивное сканирование Program Files на всех дисках выполнялось на
/// каждый вызов резолвера контекста запуска («первый вход в редактор ~25 секунд»,
/// «пробел — 2-3 секунды тормозов»). Кэш возвращает ранее вычисленный список в течение
/// TTL и сбрасывается при смене путей поиска/явной инвалидации.
/// </summary>
public sealed class PlatformVersionCacheTests
{
    [Fact]
    public void FindPlatformVersionDirs_SecondCallWithinTtl_ReturnsCachedInstance()
    {
        // Повторный вызов с той же разрядностью НЕ сканирует ФС заново — cached.Entries.
        var first = PlatformVersionService.FindPlatformVersionDirs("64");
        var second = PlatformVersionService.FindPlatformVersionDirs("64");

        Assert.Same(first, second);
    }

    [Fact]
    public void InvalidateSearchCache_ForcesRecompute()
    {
        var first = PlatformVersionService.FindPlatformVersionDirs("32");

        PlatformVersionService.InvalidateSearchCache();

        var after = PlatformVersionService.FindPlatformVersionDirs("32");
        Assert.NotSame(first, after);

        // Убрать следы кэша для других тестов (платформы могли измениться).
        PlatformVersionService.InvalidateSearchCache();
    }

    [Fact]
    public void SetAdditionalSearchPaths_InvalidatesPlatformVersionDirsCache()
    {
        var previous = PlatformVersionService.GetAdditionalSearchPaths();
        try
        {
            var first = PlatformVersionService.FindPlatformVersionDirs("64");

            PlatformVersionService.SetAdditionalSearchPaths(new[] { System.IO.Path.GetTempPath() });

            var after = PlatformVersionService.FindPlatformVersionDirs("64");
            Assert.NotSame(first, after);
        }
        finally
        {
            PlatformVersionService.SetAdditionalSearchPaths(previous);
            PlatformVersionService.InvalidateSearchCache();
        }
    }

    [Fact]
    public void ResolveVersionBinDirectory_SameVersionSecondCall_ReturnsCachedResult()
    {
        // Каталог bin для полной версии резолвится один раз (TTL) — результат запоминается.
        var before = PlatformVersionService.ResolveVersionBinDirectory("9.9.9.999", "64");
        PlatformVersionService.InvalidateSearchCache();
        var after = PlatformVersionService.ResolveVersionBinDirectory("9.9.9.999", "64");

        // Несуществующая версия даёт null и до, и после инвалидации (кэш не искажает результат).
        Assert.Equal(before, after);
    }
}