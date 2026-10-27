using System.Collections.Generic;
using System.Linq;
using Workspace1C.Models;

namespace Workspace1C.ViewModels;

/// <summary>Тип элемента палитры команд.</summary>
public enum CommandPaletteItemKind
{
    /// <summary>Информационная база: Enter запускает «1С:Предприятие», Shift+Enter — Конфигуратор.</summary>
    Base,

    /// <summary>Команда интерфейса (настройки, синхронизация, окна-утилиты и т.п.).</summary>
    Command
}

/// <summary>Элемент палитры: база или команда.</summary>
public sealed class CommandPaletteItem
{
    /// <summary>Тип элемента.</summary>
    public CommandPaletteItemKind Kind { get; init; }

    /// <summary>Идентификатор базы (Kind=Base) или команды (Kind=Command).</summary>
    public string Id { get; init; } = "";

    /// <summary>Заголовок: имя базы или название команды.</summary>
    public string Title { get; init; } = "";

    /// <summary>Пояснение: группа/подключение базы или подсказка команды.</summary>
    public string Subtitle { get; init; } = "";

    /// <summary>
    /// Дополнительный текст поиска (п.8 «Заметки к базе», 1.0.0.90): markdown-карточка
    /// заметки базы. Участвует в фильтрации, но НЕ показывается в строке палитры.
    /// </summary>
    public string SearchText { get; init; } = "";

    /// <summary>Подсказка режима запуска для базы («Enter — Предприятие, Shift+Enter — Конфигуратор»).</summary>
    public string Hint { get; init; } = "";
}

/// <summary>
/// Чистая логика командной палитры (Ctrl+K): источник элементов, фильтрация
/// запросом и порядок выдачи. Не зависит от платформы и UI — покрыта
/// юнит-тестами. Порядок выдачи: базы (совпадение с начала имени выше
/// вхождения, избранные с ненулевым слотом выше обычных), затем команды.
/// </summary>
public sealed class CommandPaletteViewModel
{
    private readonly List<CommandPaletteItem> _bases = new();
    private readonly List<CommandPaletteItem> _commands = new();

    /// <summary>Элементы, видимые при текущем запросе.</summary>
    public List<CommandPaletteItem> VisibleItems { get; private set; } = new();

    /// <summary>Индекс выбранного элемента в <see cref="VisibleItems"/>; синхронизируется окном.</summary>
    public int SelectedIndex { get; set; }

    /// <summary>Текущий выбранный элемент (null, если список пуст).</summary>
    public CommandPaletteItem? Current =>
        SelectedIndex >= 0 && SelectedIndex < VisibleItems.Count ? VisibleItems[SelectedIndex] : null;

    /// <summary>Текущий запрос (для восстановления состояния окна).</summary>
    public string Query { get; private set; } = "";

    /// <summary>Заменяет источник элементов: базы и команды. Null/пустые источники безопасны.</summary>
    public void SetSource(IEnumerable<CommandPaletteItem>? bases, IEnumerable<CommandPaletteItem>? commands)
    {
        _bases.Clear();
        if (bases is not null) _bases.AddRange(bases);
        _commands.Clear();
        if (commands is not null) _commands.AddRange(commands);
        ApplyQuery(Query);
    }

    /// <summary>Применяет запрос: при смене текста выбор сбрасывается на первый элемент.</summary>
    public void ApplyQuery(string query)
    {
        var newQuery = query ?? "";
        var queryChanged = !string.Equals(newQuery, Query, System.StringComparison.Ordinal);
        Query = newQuery;

        var visible = Filter(_bases, Query).Concat(Filter(_commands, Query)).ToList();
        VisibleItems = visible;

        if (queryChanged)
            SelectedIndex = 0;
        if (SelectedIndex >= visible.Count)
            SelectedIndex = visible.Count - 1;
        if (SelectedIndex < 0 && visible.Count > 0)
            SelectedIndex = 0;
    }

    /// <summary>Сбрасывает запрос и выбор (при открытии палитры).</summary>
    public void Reset()
    {
        Query = "";
        SelectedIndex = 0;
        ApplyQuery("");
    }

    /// <summary>Выбор вниз; возвращает true, если позиция изменилась.</summary>
    public bool MoveDown()
    {
        if (SelectedIndex >= VisibleItems.Count - 1)
            return false;
        SelectedIndex++;
        return true;
    }

    /// <summary>Выбор вверх; возвращает true, если позиция изменилась.</summary>
    public bool MoveUp()
    {
        if (SelectedIndex <= 0)
            return false;
        SelectedIndex--;
        return true;
    }

    /// <summary>
    /// Фильтр одного списка: пустой запрос возвращает всё; иначе — элементы,
    /// у которых запрос встречается в заголовке или пояснении (без учёта
    /// регистра; каждый «слово» запроса должно найтись). Ранжирование:
    /// совпадение с начала заголовка выше простого вхождения.
    /// </summary>
    internal static List<CommandPaletteItem> Filter(List<CommandPaletteItem> source, string query)
    {
        var q = (query ?? "").Trim();
        if (q.Length == 0)
            return source.ToList();

        var words = q.Split(' ', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
        var matched = new List<(CommandPaletteItem Item, int Rank)>();

        foreach (var item in source)
        {
            // Поиск ведётся по заголовку, пояснению и дополнительному тексту (заметка базы).
            var title = item.Title ?? "";
            var subtitle = item.Subtitle ?? "";
            var searchText = item.SearchText ?? "";
            var ok = true;
            var rank = 0;

            foreach (var word in words)
            {
                var inTitle = title.IndexOf(word, System.StringComparison.OrdinalIgnoreCase);
                var inSubtitle = subtitle.IndexOf(word, System.StringComparison.OrdinalIgnoreCase);
                var inSearch = searchText.IndexOf(word, System.StringComparison.OrdinalIgnoreCase);
                if (inTitle < 0 && inSubtitle < 0 && inSearch < 0)
                {
                    ok = false;
                    break;
                }
                if (inTitle == 0)
                    rank += 3;
                else if (inTitle > 0)
                    rank += 2;
                if (inSubtitle >= 0)
                    rank += 1;
                // Совпадение только в заметке — низший приоритет (не вытесняет имя/группу).
                if (inSearch >= 0)
                    rank += 0;
                if (inSubtitle < 0 && inTitle < 0 && inSearch >= 0)
                    rank += 1;
            }

            if (ok)
                matched.Add((item, rank));
        }

        return matched
            .OrderByDescending(m => m.Rank)
            .ThenBy(m => m.Item.Title, System.StringComparer.OrdinalIgnoreCase)
            .Select(m => m.Item)
            .ToList();
    }
}
