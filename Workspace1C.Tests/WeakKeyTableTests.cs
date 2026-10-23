using System.Runtime.CompilerServices;
using Workspace1C.Collections;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #21: метрики компактного режима хранятся в таблицах со слабыми ключами —
/// строки списка, ушедшие из визуального дерева при переключении вкладок, не
/// удерживаются в памяти (раньше статические Dictionary держали их навсегда).
/// </summary>
public sealed class WeakKeyTableTests
{
    private sealed class Key { }

    [Fact]
    public void DictionaryApi_Works()
    {
        var table = new WeakKeyTable<Key, double>();
        var k = new Key();
        Assert.True(table.IsEmpty);
        Assert.False(table.ContainsKey(k));

        table[k] = 1.5;
        Assert.True(table.ContainsKey(k));
        Assert.True(table.TryGetValue(k, out var v));
        Assert.Equal(1.5, v);
        Assert.False(table.IsEmpty);
        Assert.Equal(1, table.Count);

        table[k] = 2.5;
        Assert.Equal(2.5, table[k]);
        Assert.Equal(1, table.Count);

        Assert.True(table.Remove(k));
        Assert.True(table.IsEmpty);
        GC.KeepAlive(k);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AddUnreferencedKeys(WeakKeyTable<Key, double> table, int count)
    {
        WeakReference? first = null;
        for (var i = 0; i < count; i++)
        {
            var k = new Key();
            first ??= new WeakReference(k);
            table[k] = i;
        }
        return first!;
    }

    [Fact]
    public void Keys_AreNotKeptAlive()
    {
        var table = new WeakKeyTable<Key, double>();
        var weak = AddUnreferencedKeys(table, 100);

        for (var i = 0; i < 3 && weak.IsAlive; i++)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
        }

        Assert.False(weak.IsAlive);
        Assert.True(table.Count < 100);
    }

    [Fact]
    public void ThemeManager_UsesWeakTables()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Workspace1C", "Themes")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var code = File.ReadAllText(Path.Combine(dir!.FullName, "Workspace1C", "Themes", "ThemeManager.cs"));
        Assert.DoesNotContain("Dictionary<FrameworkElement, CompactMetric>", code);
        Assert.DoesNotContain("Dictionary<ColumnDefinition, double>", code);
        Assert.Contains("WeakKeyTable<FrameworkElement, CompactMetric>", code);
    }
}
