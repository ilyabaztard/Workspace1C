using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Workspace1C.Collections;

/// <summary>
/// Таблица «объект → значение» со СЛАБЫМИ ключами (обёртка над
/// <see cref="ConditionalWeakTable{TKey, TValue}"/> с API словаря).
/// Issue #21: метрики компактного режима (<c>ThemeManager</c>) хранились в статических
/// <see cref="Dictionary{TKey, TValue}"/> с сильными ссылками на элементы строк списка
/// баз. Строки, ушедшие из визуального дерева при переключении вкладок «Все /
/// Избранное / Недавние», оставались в словарях навсегда вместе со всем деревом
/// строки — память росла на несколько мегабайт за каждое переключение, а переключение
/// замедлялось. Слабые ключи отпускают запись вместе с самим элементом.
/// </summary>
public sealed class WeakKeyTable<TKey, TValue> where TKey : class
{
    private ConditionalWeakTable<TKey, StrongBox<TValue>> _table = new();

    /// <summary>Есть ли запись для ключа.</summary>
    public bool ContainsKey(TKey key) => _table.TryGetValue(key, out _);

    /// <summary>Значение по ключу; запись добавляет или заменяет значение.</summary>
    public TValue this[TKey key]
    {
        get => _table.TryGetValue(key, out var box) ? box.Value! : throw new KeyNotFoundException();
        set => _table.AddOrUpdate(key, new StrongBox<TValue>(value));
    }

    /// <summary>Получает значение по ключу.</summary>
    public bool TryGetValue(TKey key, out TValue value)
    {
        if (_table.TryGetValue(key, out var box))
        {
            value = box.Value!;
            return true;
        }
        value = default!;
        return false;
    }

    /// <summary>Удаляет запись по ключу.</summary>
    public bool Remove(TKey key) => _table.Remove(key);

    /// <summary>Нет ни одной живой записи.</summary>
    public bool IsEmpty
    {
        get
        {
            foreach (var _ in (IEnumerable<KeyValuePair<TKey, StrongBox<TValue>>>)_table)
                return false;
            return true;
        }
    }

    /// <summary>Число живых записей (перебор; для диагностики и тестов).</summary>
    public int Count
    {
        get
        {
            var n = 0;
            foreach (var _ in (IEnumerable<KeyValuePair<TKey, StrongBox<TValue>>>)_table)
                n++;
            return n;
        }
    }

    /// <summary>Удаляет все записи.</summary>
    public void Clear() => _table.Clear();
}
