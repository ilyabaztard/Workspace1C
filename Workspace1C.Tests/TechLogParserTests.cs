using Workspace1C.Services.TechLogParser;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты анализа технологического журнала (выпуск 1.0.0.86, п.4): разбор строк
/// журнала (в т.ч. минимальных и нестандартных), топы долгих операций, сводка
/// блокировок, группировка исключений по нормализованному контексту, фильтры
/// (база/пользователь/период) и экспорт в CSV. Только чистые функции — без UI.
/// </summary>
public sealed class TechLogParserTests
{
    [Fact]
    public void ParseEvent_SimpleDbmsSql()
    {
        const string line =
            "20250115 143602.871-28472-17824 duration=110587 process=1cv8 usr=Иванов " +
            "event=DBMSSQL DBMSSQL: SELECT TOP 1 * FROM _Reference171 WHERE _Fld1 = 123";

        var events = TechLogReader.ParseEventsText(line);
        var ev = Assert.Single(events);

        Assert.Equal("DBMSSQL", ev.EventType);
        Assert.Equal(110587, ev.DurationUs);
        Assert.Equal("1cv8", ev.Process);
        Assert.Equal("Иванов", ev.User);
        Assert.Equal(new DateTime(2025, 1, 15, 14, 36, 2, 871), ev.Timestamp);
        Assert.Contains("SELECT TOP 1 * FROM _Reference171", ev.Context);
        Assert.Equal(110.587, ev.DurationMs, precision: 3);
    }

    [Fact]
    public void ParseEvent_MinimalLineWithoutDurationAndUser()
    {
        const string line = "20250115 143602-1-2 event=EXCP EXCP: {";

        var events = TechLogReader.ParseEventsText(line);
        var ev = Assert.Single(events);

        Assert.Equal("EXCP", ev.EventType);
        Assert.Equal(0, ev.DurationUs);
        Assert.Equal(string.Empty, ev.User);
        Assert.Equal(string.Empty, ev.Process);
        Assert.Equal("EXCP: {", ev.Context);
        Assert.Equal(new DateTime(2025, 1, 15, 14, 36, 2), ev.Timestamp);
    }

    [Fact]
    public void ParseStream_MultilineContextIsCollected()
    {
        const string text =
            "20250115 143602.100-3-4 duration=0 process=1cv8 usr=Иванов event=EXCP EXCP: {\n" +
            "Исключение: Деление на ноль\n" +
            "}\n" +
            "20250115 143603.200-3-4 duration=5 process=1cv8 event=SDBL SDBL: Запрос.Выполнить\n";

        var events = TechLogReader.ParseEventsText(text);

        Assert.Equal(2, events.Count);
        var lines = events[0].Context.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        Assert.Equal("EXCP: {", lines[0]);
        Assert.Contains("Исключение: Деление на ноль", lines);
        Assert.Contains("}", lines);
        Assert.Equal("SDBL", events[1].EventType);
        Assert.Equal(5, events[1].DurationUs);
    }

    [Fact]
    public void ParseStream_LinesWithoutDatePrefixContinueCurrentEvent()
    {
        const string text = "мусор без даты\n" +
                            "20250115 143602.100-1-2 event=EXCP X\n" +
                            "ещё строка\n";

        var events = TechLogReader.ParseEventsText(text);
        var ev = Assert.Single(events);

        Assert.Equal("EXCP", ev.EventType);
        Assert.StartsWith("X", ev.Context);
        Assert.Contains("ещё строка", ev.Context);
    }

    [Fact]
    public void ParseEvent_ResolvesBaseFromProcessName()
    {
        const string text = "20250115 100000.100-10-20 p:processName=БухгалтерияПредприятия_ДЕМО " +
                            "usr=Иванов event=CALL 1C:Enterprise.Application";

        var events = TechLogReader.ParseEventsText(text);
        var ev = Assert.Single(events);

        Assert.Equal("БухгалтерияПредприятия_ДЕМО", ev.Base);
    }

    [Fact]
    public void Query_MatchesByUserAndBase()
    {
        const string text =
            "20250115 100000.100-1-1 p:base=Демо_файловая usr=Иванов event=CALL X\n" +
            "20250115 100000.100-1-1 process=1cv8:Бухгалтерия usr=Петров event=CALL Y\n";

        var events = TechLogReader.ParseEventsText(text);
        Assert.Equal(2, events.Count);

        var byUser = new TechLogQuery { UserFilter = "иванов" };
        Assert.Single(events, byUser.Matches);

        var byBase = new TechLogQuery { BaseFilter = "бухгалтерия" };
        Assert.Single(events, byBase.Matches);
    }

    [Fact]
    public void Query_MatchesPeriod()
    {
        const string text =
            "20250115 100000-1-1 event=SDBL A\n" +
            "20250115 110000-1-1 event=SDBL B\n";

        var events = TechLogReader.ParseEventsText(text);
        var query = new TechLogQuery
        {
            From = new DateTime(2025, 1, 15, 10, 30, 0),
            To = new DateTime(2025, 1, 15, 11, 30, 0)
        };

        var matched = events.Where(query.Matches).ToList();
        var ev = Assert.Single(matched);
        Assert.Equal("SDBL", ev.EventType);
        Assert.Equal("B", ev.Context);
    }

    [Fact]
    public void Analyze_TopCallsAreGroupedByTextAndSortedByTotalDuration()
    {
        const string text =
            "20250115 100000.000-1-1 duration=1000000 event=CALL A.doWork\n" +
            "20250115 100000.100-1-1 duration=2000000 event=CALL A.doWork\n" +
            "20250115 100000.200-1-1 duration=5000000 event=CALL B.doSlow\n";

        var events = TechLogReader.ParseEventsText(text);
        var result = TechLogAnalyzer.Analyze(events);

        Assert.Equal(2, result.TopCalls.Count);
        Assert.Equal("B.doSlow", result.TopCalls[0].Text);
        Assert.Equal(1, result.TopCalls[0].Count);
        Assert.Equal(5000, result.TopCalls[0].TotalDurationMs, precision: 3);

        Assert.Equal("A.doWork", result.TopCalls[1].Text);
        Assert.Equal(2, result.TopCalls[1].Count);
        Assert.Equal(3000, result.TopCalls[1].TotalDurationMs, precision: 3);
        Assert.Equal(2000, result.TopCalls[1].MaxDurationMs, precision: 3);
    }

    [Fact]
    public void Analyze_IncludesDbAndSdblTops()
    {
        const string text =
            "20250115 100000.000-1-1 duration=9000000 event=DBMSSQL SELECT * FROM T\n" +
            "20250115 100000.100-1-1 duration=8000000 event=DBPOSTGRS SELECT * FROM P\n" +
            "20250115 100000.200-1-1 duration=7000000 event=SDBL Запрос.Выполнить\n";

        var events = TechLogReader.ParseEventsText(text);
        var result = TechLogAnalyzer.Analyze(events);

        Assert.Equal(2, result.TopSql.Count); // DBMSSQL + DBPOSTGRS в одном топе
        Assert.Single(result.TopSdbl);
        Assert.Equal("SELECT * FROM T", result.TopSql[0].Text);
    }

    [Fact]
    public void Analyze_MinDurationAppliesToTopsButNotToExceptions()
    {
        const string text =
            "20250115 100000.000-1-1 duration=5000 event=CALL quick\n" +
            "20250115 100000.100-1-1 duration=4000000 event=CALL slow\n" +
            "20250115 100000.200-1-1 duration=100000 event=EXCP Деление на ноль\n";

        var events = TechLogReader.ParseEventsText(text);
        var result = TechLogAnalyzer.Analyze(events, new TechLogQuery { MinDurationUs = 1000000 });

        var call = Assert.Single(result.TopCalls);
        Assert.Equal("slow", call.Text);
        Assert.Equal(1, result.CountExceptions); // исключение проходит, несмотря на порог
        Assert.Single(result.Exceptions);
    }

    [Fact]
    public void Analyze_GroupsExceptionsByNormalizedContext()
    {
        const string text =
            "20250115 140000.100-1-1 duration=123456 event=EXCP " +
            "Исключение: Ошибка получения данных по ссылке, duration=123456, время 12:34:56, таблица #tmp123\n" +
            "20250115 140001.100-1-1 duration=999999 event=EXCP " +
            "Исключение: Ошибка получения данных по ссылке, duration=999999, время 01:02:03, таблица #tmp456\n" +
            "20250115 140002.100-1-1 duration=1 event=EXCP Деление на ноль duration=1\n";

        var events = TechLogReader.ParseEventsText(text);
        var result = TechLogAnalyzer.Analyze(events);

        Assert.Equal(2, result.Exceptions.Count);

        var dataError = result.Exceptions.First(e => e.Key.Contains("Ошибка получения данных", StringComparison.Ordinal));
        Assert.Equal(2, dataError.Count);

        var zeroDivision = result.Exceptions.First(e => e.Key.Contains("Деление на ноль", StringComparison.Ordinal));
        Assert.Equal(1, zeroDivision.Count);

        Assert.DoesNotContain("123456", dataError.Key);
        Assert.DoesNotContain("#tmp", dataError.Key);
        Assert.DoesNotContain("12:34:56", zeroDivision.Key);
    }

    [Fact]
    public void NormalizeExceptionContext_RemovesDynamicParts()
    {
        const string context = "Длительность: 0.5 с, в 12:34:56, хеш 8F3B9A2C1D, таблица #tmp99";

        var normalized = TechLogAnalyzer.NormalizeExceptionContext(context);

        Assert.Contains("Длительность: <N> с", normalized);
        Assert.Contains("<T>", normalized);
        Assert.Contains("#tmp99".Replace("#tmp99", "<TMP>"), normalized);
        Assert.DoesNotContain("8F3B9A2C1D", normalized);
    }

    [Fact]
    public void Analyze_GroupsLocksByObjects()
    {
        const string text =
            "20250115 100000.100-1-1 duration=100000 event=TLOCK TLOCK: Connections=1,2,3 Objects=Справочник.Контрагенты\n" +
            "20250115 100001.100-1-1 duration=50000 event=TLOCK TLOCK: Connections=4 Objects=Справочник.Контрагенты\n" +
            "20250115 100002.100-1-1 duration=0 event=TDEADLOCK TDEADLOCK: жертва Objects=Документ.ПриходнаяНакладная\n";

        var events = TechLogReader.ParseEventsText(text);
        var result = TechLogAnalyzer.Analyze(events);

        Assert.Equal(2, result.Locks.Count);
        Assert.Equal(1, result.CountDeadlocks);

        var tlock = result.Locks.First(r => r.EventType == "TLOCK");
        Assert.Equal(2, tlock.Count);
        Assert.Equal("Справочник.Контрагенты", tlock.Key);
        Assert.Equal(150, tlock.TotalDurationMs, precision: 3);
    }

    [Fact]
    public void CsvBuilder_BuildsDocumentWithSectionsAndEscapes()
    {
        const string text =
            "20250115 100000.100-1-1 duration=12000000 event=DBMSSQL SELECT \"a\" FROM T; \"x\"\n" +
            "20250115 100001.100-1-1 duration=1000 event=EXCP Ошибка; в описании\n";

        var events = TechLogReader.ParseEventsText(text);
        var result = TechLogAnalyzer.Analyze(events);
        var document = TechLogCsvBuilder.BuildDocument(result, "C:\\logs");

        Assert.Contains("Workspace 1C — TechLog analysis", document);
        Assert.Contains("Top CALL", document);
        Assert.Contains("Top DBMSSQL (DB*)", document);
        Assert.Contains("Top SDBL", document);
        Assert.Contains("Locks (TLOCK/TDEADLOCK)", document);
        Assert.Contains("Exceptions (EXCP)", document);
        Assert.Contains("Text;Count;MaxDurationMs;AvgDurationMs;TotalDurationMs", document);

        // Значение с разделителем «;» и кавычками экранируется по RFC 4180.
        Assert.Contains("\"Ошибка; в описании\"", document);
        Assert.Contains("\"SELECT \"\"a\"\" FROM T; \"\"x\"\"\"", document);
    }

    [Fact]
    public void Analyze_EmptyEventsProducesEmptyResult()
    {
        var result = TechLogAnalyzer.Analyze(Array.Empty<TechLogEvent>());

        Assert.Empty(result.TopCalls);
        Assert.Empty(result.TopSql);
        Assert.Empty(result.TopSdbl);
        Assert.Empty(result.Locks);
        Assert.Empty(result.Exceptions);
        Assert.Equal(0, result.TotalEvents);
        Assert.Null(result.FirstSeen);
    }
}