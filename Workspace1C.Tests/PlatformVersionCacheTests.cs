using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты TTL-кэша поиска каталогов платформ (issue #67, производительность подстановок
/// сценариев): рекурсивное сканирование Program Files на всех дисках выполнялось на
/// каждый вызов резолвера контекста запуска («первый вход в редактор ~25 секунд»,
/// «пробел — 2-3 секунды тормозов»). Кэш возвращает ранее вычисленный список в течение
/// TTL и сбрасывается при смене путей поиска/явной инвалидации.
/// <para>
/// Кэш глобальный и делится на всю сборку (и с <c>OneCPlatformLocatorPlatformVersionTests</c>),
/// поэтому тесты, которые инвалидируют его глобально, изолированы в одну xUnit-коллекцию с
/// <c>DisableParallelization</c>: внутри коллекции тесты не гоняются параллельно друг с другом,
/// и «Assert.Same»/«Assert.NotSame» не флакают из-за сброса кэша между вызовами (починка
/// флака 1.0.0.74/1.0.0.75: ResolveVersionBinDirectory_SameVersionSecondCall_ReturnsCachedResult).
/// </para>
/// </summary>
[CollectionDefinition("PlatformVersionCache", DisableParallelization = true)]
public sealed class PlatformVersionCacheCollection
{
}

/// <summary>Тесты глобального кэша платформ — изолированная коллекция (см. определение выше).</summary>
[Collection("PlatformVersionCache")]
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
        // Каталог bin для полной версии резолвится один раз (TTL). Версия заведомо
        // несуществующая: последовательные вызовы обязаны давать одинаковый (null)
        // результат — кэш не искажает его, и тест не зависит от глобальной инвалидации
        // кэша другими тестами (в середину больше не вставляется InvalidateSearchCache,
        // который при параллельном прогоне «съедался»/пересекался с другими коллекциями).
        var before = PlatformVersionService.ResolveVersionBinDirectory("9.9.9.999", "64");
        var after = PlatformVersionService.ResolveVersionBinDirectory("9.9.9.999", "64");

        Assert.Equal(before, after);
        Assert.Null(before);
    }
}