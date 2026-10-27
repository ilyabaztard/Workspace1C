using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Результат планирования «добавляющего» импорта снимка списка баз (0.3.9.122):
/// что будет добавлено и сколько дубликатов пропущено.
/// </summary>
/// <param name="BasesToAdd">Базы из файла, которых нет в текущем списке (по строке подключения).</param>
/// <param name="GroupsToAdd">Группы из файла, которых нет в текущем списке (по имени, с иерархией ParentId).</param>
/// <param name="TagsToAdd">Новые теги (отсутствующие в текущем списке), встречающиеся у добавляемых баз.</param>
/// <param name="DuplicatesSkipped">Сколько баз из файла пропущено: такая строка подключения уже есть в списке.</param>
public sealed record InfobaseImportPlan(
    List<Infobase> BasesToAdd,
    List<Group> GroupsToAdd,
    List<string> TagsToAdd,
    int DuplicatesSkipped);

/// <summary>
/// Чистая логика JSON-переноса списка баз (0.3.9.122): сериализация/десериализация
/// полного снимка (<see cref="InfobaseListSnapshot"/>), планирование «добавляющего»
/// импорта с дедупликацией по строке подключения и слияние групп с сохранением
/// иерархии. Не зависит от платформы и UI — покрыта юнит-тестами.
/// </summary>
public static class InfobaseJsonTransfer
{
    /// <summary>Общие параметры JSON: нечувствительность к регистру имён свойств (как в остальном приложении).</summary>
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Ключ дубликата: нормализованная строка подключения (без учёта регистра).
    /// Файловая — путь, клиент-серверная — Srvr/Ref, веб — URL. Пустая строка —
    /// база без подключения (не считаем дубликатом ни с чем).
    /// </summary>
    public static string ConnectionKey(Infobase ib)
    {
        var connection = ib.Connection?.ToConnectionString() ?? string.Empty;
        return connection.Trim().ToUpperInvariant();
    }

    /// <summary>
    /// Формирует полный снимок списка: базы (фильтрация приватных выполняется
    /// вызывающей стороной) + группы целиком + заметки баз (п.8, 1.0.0.90).
    /// Значения секретов в снимок НЕ попадают (зашифрованы ключом текущей машины) —
    /// переносятся только markdown-карточки и имена секретов
    /// (см. <see cref="InfobaseNoteTransfer"/>).
    /// </summary>
    public static InfobaseListSnapshot BuildSnapshot(
        IEnumerable<Infobase> infobases,
        IEnumerable<Group> groups,
        IEnumerable<InfobaseNote>? notes = null)
    {
        ArgumentNullException.ThrowIfNull(infobases);
        ArgumentNullException.ThrowIfNull(groups);

        return new InfobaseListSnapshot
        {
            Version = InfobaseListSnapshot.CurrentVersion,
            Infobases = infobases.ToList(),
            Groups = groups.ToList(),
            Notes = ToTransfers(notes)
        };
    }

    /// <summary>
    /// Преобразует заметки в переносимые DTO: markdown + имена секретов (без значений).
    /// null/пустой источник — пустой список.
    /// </summary>
    public static List<InfobaseNoteTransfer> ToTransfers(IEnumerable<InfobaseNote>? notes)
    {
        var result = new List<InfobaseNoteTransfer>();
        if (notes is null)
            return result;
        foreach (var note in notes)
        {
            if (note is null || string.IsNullOrWhiteSpace(note.InfobaseId))
                continue;
            if (!note.HasContent)
                continue;
            result.Add(new InfobaseNoteTransfer
            {
                InfobaseId = note.InfobaseId,
                Markdown = note.Markdown ?? string.Empty,
                SecretNames = note.SecretNames?.ToList() ?? new List<string>(),
                UpdatedAt = note.UpdatedAt
            });
        }
        return result;
    }

    /// <summary>Сериализует снимок в JSON (читаемая кириллица, отступы).</summary>
    public static string Serialize(InfobaseListSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
        {
            WriteIndented = true,
            // Кириллицу и прочие не-ASCII символы пишем читаемыми UTF-8,
            // а не \uXXXX-последовательностями (issue #170).
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }

    /// <summary>
    /// Десериализует снимок из JSON. Понимает и старые форматы: контейнер
    /// <see cref="InfobaseExportData"/> (базы + группы) и «голый» список баз.
    /// </summary>
    public static InfobaseListSnapshot? Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        // Новый формат (полное состояние) и формат InfobaseExportData имеют
        // одинаковую структуру — пробуем снимок первым.
        try
        {
            var snapshot = JsonSerializer.Deserialize<InfobaseListSnapshot>(json, ReadOptions);
            if (snapshot is not null && snapshot.Infobases.Count > 0)
                return snapshot;
        }
        catch (JsonException)
        {
            // Падаем ниже к старому формату.
        }

        // Старый формат: в файле лежит только список баз.
        var plain = JsonSerializer.Deserialize<List<Infobase>>(json, ReadOptions);
        if (plain is null || plain.Count == 0)
            return null;

        return new InfobaseListSnapshot
        {
            Version = 1,
            Infobases = plain,
            Groups = new List<Group>()
        };
    }

    /// <summary>
    /// Планирует «добавляющий» импорт: базы, которых ещё нет по строке подключения, —
    /// к добавлению; дубликаты — в счётчик. Группы добавляются только недостающие
    /// (по имени, без учёта регистра). Новые теги — те, что встречаются у добавляемых
    /// баз и отсутствуют в текущем списке.
    /// </summary>
    public static InfobaseImportPlan PlanImport(
        IEnumerable<Infobase> existingBases,
        IEnumerable<Group> existingGroups,
        IEnumerable<string> existingTags,
        InfobaseListSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(existingBases);
        ArgumentNullException.ThrowIfNull(existingGroups);
        ArgumentNullException.ThrowIfNull(existingTags);
        ArgumentNullException.ThrowIfNull(snapshot);

        var existingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ib in existingBases)
        {
            var key = ConnectionKey(ib);
            if (key.Length > 0)
                existingKeys.Add(key);
        }

        var basesToAdd = new List<Infobase>();
        var duplicatesSkipped = 0;
        foreach (var candidate in snapshot.Infobases)
        {
            var key = ConnectionKey(candidate);
            if (key.Length > 0 && existingKeys.Contains(key))
            {
                duplicatesSkipped++;
                continue;
            }

            basesToAdd.Add(candidate);
            if (key.Length > 0)
                existingKeys.Add(key);
        }

        var existingGroupNames = new HashSet<string>(
            existingGroups.Select(g => (g.Name ?? string.Empty).Trim()),
            StringComparer.OrdinalIgnoreCase);
        var groupsToAdd = new List<Group>();
        foreach (var group in snapshot.Groups)
        {
            var name = (group.Name ?? string.Empty).Trim();
            if (name.Length == 0)
                continue;
            if (existingGroupNames.Add(name))
                groupsToAdd.Add(group);
        }

        var existingTagSet = new HashSet<string>(
            existingTags.Select(t => (t ?? string.Empty).Trim()),
            StringComparer.OrdinalIgnoreCase);
        var tagsToAdd = new List<string>();
        foreach (var ib in basesToAdd)
        {
            foreach (var tag in ib.Tags)
            {
                var trimmed = (tag ?? string.Empty).Trim();
                if (trimmed.Length == 0 || !existingTagSet.Add(trimmed))
                    continue;
                tagsToAdd.Add(trimmed);
            }
        }

        return new InfobaseImportPlan(basesToAdd, groupsToAdd, tagsToAdd, duplicatesSkipped);
    }

    /// <summary>
    /// Клонирует группы снапшота для добавления в текущий список, сохраняя иерархию
    /// ParentId. Если Id группы уже занят существующей группой (или пуст), группе
    /// назначается новый GUID, а ParentId потомков переписывается через маппинг —
    /// конфликты Id не ломают дерево.
    /// </summary>
    public static List<Group> MergeNewGroups(IReadOnlyCollection<Group> existingGroups, IEnumerable<Group> incoming)
    {
        ArgumentNullException.ThrowIfNull(existingGroups);
        ArgumentNullException.ThrowIfNull(incoming);

        var idTaken = new HashSet<string>(
            existingGroups.Select(g => (g.Id ?? string.Empty).Trim())
                .Where(id => id.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        // Маппинг: старый Id из файла → фактический Id добавляемой группы.
        var remap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var added = new List<Group>();

        foreach (var g in incoming)
        {
            var clone = new Group
            {
                Id = (g.Id ?? string.Empty).Trim(),
                Name = g.Name ?? string.Empty,
                ParentId = g.ParentId ?? string.Empty,
                Description = g.Description ?? string.Empty,
                Color = string.IsNullOrWhiteSpace(g.Color) ? "#2D6CDF" : g.Color,
                IconColor = string.IsNullOrWhiteSpace(g.IconColor) ? "#FFFFFF" : g.IconColor,
                Icon = g.Icon ?? string.Empty
            };

            if (clone.Id.Length == 0 || idTaken.Contains(clone.Id))
            {
                // Один GUID на группу: он же становится новым Id, он же пишется в маппинг
                // для переписывания ParentId потомков — иначе ссылки разъедутся.
                var newId = Guid.NewGuid().ToString("D").ToUpperInvariant();
                if (clone.Id.Length > 0)
                    remap[clone.Id] = newId;
                clone.Id = newId;
            }

            idTaken.Add(clone.Id);
            added.Add(clone);
        }

        // Переписываем ParentId: ссылки на группы файла, получившие новый Id,
        // должны указывать на фактически добавленные группы.
        foreach (var group in added)
        {
            var parentId = group.ParentId.Trim();
            if (parentId.Length > 0 && remap.TryGetValue(parentId, out var mapped))
                group.ParentId = mapped;
        }

        return added;
    }
}