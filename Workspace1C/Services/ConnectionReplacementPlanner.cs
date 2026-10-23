using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Область применения массовой замены в строках подключения.
/// </summary>
public enum ConnectionReplaceScope
{
    /// <summary>Все видимые базы.</summary>
    AllBases,

    /// <summary>Базы из набора Id мультивыделения (<see cref="Infobase.Id"/>).</summary>
    BatchSelected,

    /// <summary>Базы текущей группы (по значению <see cref="Infobase.Group"/>).</summary>
    CurrentGroup
}

/// <summary>
/// Строка предпросмотра массовой замены: база, поле и «было → станет».
/// </summary>
/// <param name="Infobase">База, к которой применяется замена.</param>
/// <param name="Field">Поле строки подключения (для <see cref="ConnectionField.Any"/> — вся строка).</param>
/// <param name="BeforeText">Текущее значение поля (или вся строка для <see cref="ConnectionField.Any"/>).</param>
/// <param name="AfterText">Новое значение после замены.</param>
/// <param name="Changed">true — значение реально изменится.</param>
/// <param name="IsPlatformChange">
/// true — строка описывает смену версии платформы (issue #54): <paramref name="Field"/>
/// не используется, <paramref name="BeforeText"/>/<paramref name="AfterText"/> — старая
/// и новая версии платформы. Отображается в колонке «Поле» как «Платформа».
/// </param>
public sealed record ConnectionReplacePreviewRow(
    Infobase Infobase,
    ConnectionField Field,
    string BeforeText,
    string AfterText,
    bool Changed,
    bool IsPlatformChange = false);

/// <summary>
/// План замены: затронутые строки предпросмотра и сводка счётчиков.
/// </summary>
/// <param name="Rows">Строки предпросмотра; только с <see cref="ConnectionReplacePreviewRow.Changed"/> == true.</param>
/// <param name="AffectedCount">Число уникальных изменяемых баз (без дублей по экземпляру базы).</param>
/// <param name="NoMatchCount">Число кандидатов с подключением, у которых совпадение не найдено.</param>
/// <param name="EmptyConnectionCount">Число кандидатов без строки подключения (не участвуют).</param>
public sealed record ConnectionReplacePlan(
    IReadOnlyList<ConnectionReplacePreviewRow> Rows,
    int AffectedCount,
    int NoMatchCount,
    int EmptyConnectionCount);

/// <summary>
/// Запись для отката одной замены: прежние и новые настройки подключения базы и
/// (при смене платформы, issue #54) прежняя/новая версии платформы.
/// <see cref="Before"/> — глубокая копия прежних настроек (снята до мутации), чтобы
/// откат не зависел от последующих правок полей базы.
/// </summary>
/// <param name="Infobase">База, чьё подключение/платформа были изменены.</param>
/// <param name="Before">Глубокая копия прежних настроек подключения.</param>
/// <param name="After">Копия новых настроек подключения.</param>
/// <param name="BeforePlatform">
/// Прежняя версия платформы (заполняется только при смене платформы; null — запись
/// не трогает <see cref="Infobase.PlatformVersion"/>).
/// </param>
/// <param name="AfterPlatform">Новая версия платформы (см. <paramref name="BeforePlatform"/>).</param>
public sealed record ConnectionReplaceUndoEntry(
    Infobase Infobase,
    ConnectionSettings Before,
    ConnectionSettings After,
    string? BeforePlatform = null,
    string? AfterPlatform = null);

/// <summary>
/// Чистый планировщик массовой замены в строках подключения (0.3.9.188, функция 6
/// «Массовая замена в строке подключения баз»): отбор кандидатов области, построение
/// плана предпросмотра <strong>без мутаций</strong>, применение с формированием снапшота
/// для отката и восстановление через <see cref="Undo"/>. Не имеет платформенных
/// зависимостей и не знает про приватность баз — получает уже видимый список от
/// вызывающей стороны (MainViewModel фильтрует скрытые приватные базы).
/// </summary>
public static class ConnectionReplacementPlanner
{
    /// <summary>
    /// Отбирает кандидатов области из переданного «видимого» списка баз.
    /// <see cref="ConnectionReplaceScope.AllBases"/> — весь список;
    /// <see cref="ConnectionReplaceScope.BatchSelected"/> — базы, чей
    /// <see cref="Infobase.Id"/> входит в набор <paramref name="batchIds"/> (порядок —
    /// как в списке, неизвестные Id игнорируются);
    /// <see cref="ConnectionReplaceScope.CurrentGroup"/> — базы, чья группа равна
    /// <paramref name="currentGroup"/> (пустой/null текущей группы → пустой результат).
    /// </summary>
    /// <param name="visibleInfobases">Уже отфильтрованный «видимый» список баз (приватные скрыты вызывающей стороной).</param>
    /// <param name="batchIds">Набор Id для области «выделенные» (может быть null).</param>
    /// <param name="currentGroup">Группа для области «текущая группа» (может быть null/пустой).</param>
    /// <param name="scope">Область применения.</param>
    /// <returns>Новый список кандидатов (исходный список не мутируется).</returns>
    public static IReadOnlyList<Infobase> SelectCandidates(
        IReadOnlyList<Infobase> visibleInfobases,
        IReadOnlyCollection<string>? batchIds,
        string? currentGroup,
        ConnectionReplaceScope scope)
    {
        ArgumentNullException.ThrowIfNull(visibleInfobases);

        var result = new List<Infobase>();
        switch (scope)
        {
            case ConnectionReplaceScope.AllBases:
                foreach (var ib in visibleInfobases)
                {
                    if (ib is not null)
                        result.Add(ib);
                }
                break;

            case ConnectionReplaceScope.BatchSelected:
                if (batchIds is { Count: > 0 })
                {
                    var ids = new HashSet<string>(batchIds, StringComparer.Ordinal);
                    foreach (var ib in visibleInfobases)
                    {
                        if (ib is not null && ids.Contains(ib.Id))
                            result.Add(ib);
                    }
                }
                break;

            case ConnectionReplaceScope.CurrentGroup:
                if (!string.IsNullOrEmpty(currentGroup))
                {
                    foreach (var ib in visibleInfobases)
                    {
                        if (ib is not null && string.Equals(ib.Group, currentGroup, StringComparison.Ordinal))
                            result.Add(ib);
                    }
                }
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(scope));
        }

        return result;
    }

    /// <summary>
    /// Фильтрует «видимый» список баз по приватности (0.3.9.189, функция 6): приватные
    /// базы скрываются, когда профиль не разблокирован (<paramref name="canShowPrivateBases"/>
    /// == false), — единая точка фильтрации для моста в MainViewModel (см.
    /// <see cref="ConnectionReplaceScope"/>). Null-элементы отбрасываются. Не мутирует
    /// входной список.
    /// </summary>
    /// <param name="infobases">Исходный список (включая приватные базы).</param>
    /// <param name="canShowPrivateBases">true — приватные базы показываются (профиль разблокирован или без пароля).</param>
    /// <returns>Новый список видимых баз.</returns>
    public static IReadOnlyList<Infobase> FilterVisibleInfobases(
        IEnumerable<Infobase> infobases,
        bool canShowPrivateBases)
    {
        ArgumentNullException.ThrowIfNull(infobases);

        var result = new List<Infobase>();
        foreach (var infobase in infobases)
        {
            if (infobase is null)
                continue;
            if (!infobase.IsPrivate || canShowPrivateBases)
                result.Add(infobase);
        }

        return result;
    }

    /// <summary>
    /// Строит план замены БЕЗ мутации баз: для каждого кандидата применяет правило к
    /// копии настроек через <see cref="ConnectionStringEditor.TryApply"/>. Базы без
    /// строки подключения (ни одно из полей Server/Ref/FilePath/WebUrl не заполнено —
    /// каноническая строка вырожденная «Srvr="";Ref=""»/«File=""»/«WS=""») попадают в
    /// <see cref="ConnectionReplacePlan.EmptyConnectionCount"/> и в Rows не включаются.
    /// Невалидный regex-паттерн пробрасывает <see cref="ArgumentException"/> наружу
    /// (его перехватывает VM и превращает в сообщение об ошибке).
    /// </summary>
    /// <param name="candidates">Кандидаты области (результат <see cref="SelectCandidates"/>).</param>
    /// <param name="rule">Правило замены.</param>
    /// <returns>План замены: строки с Changed == true и сводка счётчиков.</returns>
    public static ConnectionReplacePlan Plan(
        IReadOnlyList<Infobase> candidates,
        ConnectionStringReplaceRule rule)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(rule);

        var rows = new List<ConnectionReplacePreviewRow>();
        var affected = new HashSet<Infobase>();
        var noMatchCount = 0;
        var emptyConnectionCount = 0;

        foreach (var infobase in candidates)
        {
            if (infobase is null)
                continue;

            if (IsEmptyConnection(infobase))
            {
                emptyConnectionCount++;
                continue;
            }

            if (TryMatch(infobase, rule, out _, out var beforeValue, out var afterValue))
            {
                rows.Add(new ConnectionReplacePreviewRow(infobase, rule.Field, beforeValue, afterValue, true));
                affected.Add(infobase);
            }
            else
            {
                noMatchCount++;
            }
        }

        return new ConnectionReplacePlan(rows, affected.Count, noMatchCount, emptyConnectionCount);
    }

    /// <summary>
    /// Применяет правило: мутирует <see cref="Infobase.Connection"/> затронутых баз
    /// (заменяет на копию новых настроек из <see cref="ConnectionStringEditor.TryApply"/>)
    /// и возвращает записи отката. Для каждой записи <see cref="ConnectionReplaceUndoEntry.Before"/>
    /// — глубокая копия ВСЕХ полей прежних настроек (Type/Server/DatabaseName/FilePath/
    /// BlockScheduledJobs/ForbidSpeechRecognition/User/Password/AuthenticationMode/Port/
    /// WebUrl), чтобы откат не зависел от последующих правок базы; <see cref="ConnectionReplaceUndoEntry.After"/>
    /// — копия новых настроек. Если правило не совпало ни в одной базе — возвращает пустой список.
    /// </summary>
    /// <param name="candidates">Кандидаты области.</param>
    /// <param name="rule">Правило замены.</param>
    /// <returns>Записи отката для затронутых баз.</returns>
    public static IReadOnlyList<ConnectionReplaceUndoEntry> Apply(
        IReadOnlyList<Infobase> candidates,
        ConnectionStringReplaceRule rule)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(rule);

        var entries = new List<ConnectionReplaceUndoEntry>();

        foreach (var infobase in candidates)
        {
            if (infobase is null || IsEmptyConnection(infobase))
                continue;

            if (TryMatch(infobase, rule, out var updated, out _, out _))
            {
                var before = DeepCopy(infobase.Connection);
                infobase.Connection = updated;
                entries.Add(new ConnectionReplaceUndoEntry(infobase, before, DeepCopy(updated)));
            }
        }

        return entries;
    }

    /// <summary>
    /// План смены платформы БЕЗ мутации баз (issue #54): для каждого кандидата, чья
    /// <see cref="Infobase.PlatformVersion"/> отличается от целевой (без учёта регистра
    /// и хвостовых пробелов), возвращает строку предпросмотра с признаком
    /// <see cref="ConnectionReplacePreviewRow.IsPlatformChange"/>. Применяется и к базам
    /// без строки подключения (платформа не связана с подключением). Пустая версия в
    /// правиле — пустой результат.
    /// </summary>
    /// <param name="candidates">Кандидаты области (результат <see cref="SelectCandidates"/>).</param>
    /// <param name="rule">Правило смены платформы.</param>
    /// <returns>Строки предпросмотра (только с реальным изменением).</returns>
    public static IReadOnlyList<ConnectionReplacePreviewRow> PlanPlatform(
        IReadOnlyList<Infobase> candidates,
        PlatformReplaceRule rule)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(rule);

        var rows = new List<ConnectionReplacePreviewRow>();
        if (string.IsNullOrWhiteSpace(rule.Platform))
            return rows;

        var target = rule.Platform.Trim();
        foreach (var infobase in candidates)
        {
            if (infobase is null)
                continue;
            if (string.Equals((infobase.PlatformVersion ?? string.Empty).Trim(), target, StringComparison.OrdinalIgnoreCase))
                continue;

            rows.Add(new ConnectionReplacePreviewRow(
                infobase,
                ConnectionField.Any,
                infobase.PlatformVersion ?? string.Empty,
                target,
                Changed: true,
                IsPlatformChange: true));
        }

        return rows;
    }

    /// <summary>
    /// Применяет смену платформы (issue #54): мутирует <see cref="Infobase.PlatformVersion"/>
    /// затронутых кандидатов и возвращает записи отката (<see cref="ConnectionReplaceUndoEntry.BeforePlatform"/>
    /// — прежняя версия, <see cref="ConnectionReplaceUndoEntry.AfterPlatform"/> — новая; настройки
    /// подключения не изменяются — Before/After — независимые копии текущего состояния).
    /// Пустая версия в правиле — no-op (пустой список). Сравнение версий — без учёта регистра
    /// и хвостовых пробелов.
    /// </summary>
    /// <param name="candidates">Кандидаты области.</param>
    /// <param name="rule">Правило смены платформы.</param>
    /// <returns>Записи отката для затронутых баз.</returns>
    public static IReadOnlyList<ConnectionReplaceUndoEntry> ApplyPlatform(
        IReadOnlyList<Infobase> candidates,
        PlatformReplaceRule rule)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(rule);

        var entries = new List<ConnectionReplaceUndoEntry>();
        if (string.IsNullOrWhiteSpace(rule.Platform))
            return entries;

        var target = rule.Platform.Trim();
        foreach (var infobase in candidates)
        {
            if (infobase is null)
                continue;
            if (string.Equals((infobase.PlatformVersion ?? string.Empty).Trim(), target, StringComparison.OrdinalIgnoreCase))
                continue;

            var beforeConnection = DeepCopy(infobase.Connection);
            var beforePlatform = infobase.PlatformVersion ?? string.Empty;
            infobase.PlatformVersion = target;
            entries.Add(new ConnectionReplaceUndoEntry(
                infobase,
                beforeConnection,
                DeepCopy(infobase.Connection),
                beforePlatform,
                target));
        }

        return entries;
    }

    /// <summary>
    /// Объединяет записи отката по базе (issue #54): если одна и та же база изменилась
    /// и по строке подключения, и по платформе (две записи), результатом становится одна
    /// запись с полным снапшотом «до/после» (Before/After — подключение; BeforePlatform/
    /// AfterPlatform — платформа). Объединение корректно при любом порядке входных записей.
    /// Порядок уникальных баз сохраняется по первому вхождению. Null-записи и записи без
    /// базы пропускаются.
    /// </summary>
    /// <param name="entries">Записи отката (из <see cref="Apply"/> и/или <see cref="ApplyPlatform"/>).</param>
    /// <returns>Новый список записей — не более одной на базу.</returns>
    public static IReadOnlyList<ConnectionReplaceUndoEntry> MergeUndoEntries(
        IEnumerable<ConnectionReplaceUndoEntry>? entries)
    {
        if (entries is null)
            return Array.Empty<ConnectionReplaceUndoEntry>();

        var result = new List<ConnectionReplaceUndoEntry>();
        var byBase = new Dictionary<Infobase, int>(ReferenceEqualityComparer.Instance);
        foreach (var entry in entries)
        {
            if (entry?.Infobase is null)
                continue;

            if (!byBase.TryGetValue(entry.Infobase, out var index))
            {
                byBase.Add(entry.Infobase, result.Count);
                result.Add(entry);
                continue;
            }

            var existing = result[index];
            result[index] = new ConnectionReplaceUndoEntry(
                entry.Infobase,
                existing.Before,
                entry.After,
                existing.BeforePlatform ?? entry.BeforePlatform,
                existing.AfterPlatform ?? entry.AfterPlatform);
        }

        return result;
    }

    /// <summary>
    /// Откат последней замены: для каждой записи восстанавливает прежние настройки
    /// (мутация объектов баз на месте — свойству <see cref="Infobase.Connection"/> присваивается
    /// снапшот <see cref="ConnectionReplaceUndoEntry.Before"/>); при смене платформы
    /// (<see cref="ConnectionReplaceUndoEntry.BeforePlatform"/> не null) восстанавливает и
    /// <see cref="Infobase.PlatformVersion"/>. Пустой/null список — no-op.
    /// </summary>
    /// <param name="entries">Записи отката из <see cref="Apply"/>.</param>
    public static void Undo(IEnumerable<ConnectionReplaceUndoEntry> entries)
    {
        if (entries is null)
            return;

        foreach (var entry in entries)
        {
            if (entry?.Infobase is null || entry.Before is null)
                continue;

            entry.Infobase.Connection = entry.Before;
            if (entry.BeforePlatform is not null)
                entry.Infobase.PlatformVersion = entry.BeforePlatform;
        }
    }

    // ---------- Вспомогательные ----------

    /// <summary>
    /// Единая точка сопоставления правила с базой: Plan (предпросмотр) и Apply (применение)
    /// используют один и тот же вызов <see cref="ConnectionStringEditor.TryApply"/>, поэтому
    /// план и результат применения не расходятся.
    /// </summary>
    private static bool TryMatch(
        Infobase infobase,
        ConnectionStringReplaceRule rule,
        out ConnectionSettings updated,
        out string beforeValue,
        out string afterValue)
        => ConnectionStringEditor.TryApply(infobase.Connection, rule, out updated, out beforeValue, out afterValue);

    /// <summary>
    /// База «без подключения»: ни одно из полей подключения Server/DatabaseName/FilePath/
    /// WebUrl не заполнено — каноническая строка вырожденная и не содержит реального
    /// подключения (Srvr="";Ref="" / File="" / WS=""). Такие базы не участвуют в замене
    /// (счётчик <see cref="ConnectionReplacePlan.EmptyConnectionCount"/>).
    /// </summary>
    private static bool IsEmptyConnection(Infobase infobase)
    {
        var connection = infobase.Connection;
        return string.IsNullOrWhiteSpace(connection.Server)
            && string.IsNullOrWhiteSpace(connection.DatabaseName)
            && string.IsNullOrWhiteSpace(connection.FilePath)
            && string.IsNullOrWhiteSpace(connection.WebUrl);
    }

    /// <summary>
    /// Независимая копия всех полей настроек подключения (строки неизменяемы — копирование
    /// по значению достаточно; снапшот не ссылается на живой объект базы).
    /// </summary>
    private static ConnectionSettings DeepCopy(ConnectionSettings source)
    {
        if (source is null)
            return new ConnectionSettings();

        return new ConnectionSettings
        {
            Type = source.Type,
            Server = source.Server,
            DatabaseName = source.DatabaseName,
            FilePath = source.FilePath,
            BlockScheduledJobs = source.BlockScheduledJobs,
            ForbidSpeechRecognition = source.ForbidSpeechRecognition,
            User = source.User,
            Password = source.Password,
            AuthenticationMode = source.AuthenticationMode,
            Port = source.Port,
            WebUrl = source.WebUrl
        };
    }
}