using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Workspace1C.Models;
using Workspace1C.Services.EventLog;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты журнала регистрации, выпуск 1.0.0.69: разбор скобочного формата 1С,
/// последовательный формат (.lgf/.lgp), отбор, представления и пресеты периода.
/// </summary>
public sealed class EventLogSequentialReaderTests
{
    private const string LgfContent =
        "1CV8LOG(ver 2.0)\r\n" +
        "f2a1b3c4-0000-0000-0000-000000000000\r\n\r\n" +
        "{1,8c8b7b6c-1111-2222-3333-444444444444,\"Иванов\",1},\r\n" +
        "{2,\"WS-01\",1},\r\n" +
        "{3,\"1CV8C\",1},\r\n" +
        "{4,\"_$Session$_.Start\",1},\r\n" +
        "{4,\"_$Data$_.Update\",2},\r\n" +
        "{5,9d9d9d9d-1111-2222-3333-444444444444,\"Справочник.Контрагенты\",1},\r\n" +
        "{6,\"srv\",1},\r\n" +
        "{7,1560,1}";

    private static string Record(string date, string severity, int eventCode, string comment, int metadata = 0,
        string transaction = "N", string presentation = "") =>
        "{" + date + "," + transaction + ",\r\n{0,0},1,1,1,1," + eventCode + "," + severity + ",\"" +
        comment.Replace("\"", "\"\"") + "\"," + metadata + ",\r\n{\"U\"},\"" + presentation + "\",1,1,0,7,0,\r\n{0}\r\n}";

    [Fact]
    public void Parser_NestedListsAndQuotedStrings()
    {
        var node = LgfBracketParser.ParseSingle("{1,{\"a\",\"b\"\"c\"},  42 ,{}}");
        Assert.NotNull(node);
        Assert.Equal(4, node!.Count);
        Assert.Equal("1", node[0]);
        var nested = Assert.IsType<List<object>>(node[1]);
        Assert.Equal("a", nested[0]);
        Assert.Equal("b\"c", nested[1]);
        Assert.Equal("42", node[2]);
        Assert.Empty(Assert.IsType<List<object>>(node[3]));
    }

    [Fact]
    public void Parser_TruncatedRecord_ReturnsNull()
    {
        Assert.Null(LgfBracketParser.ParseSingle("{1,\"unterminated"));
        Assert.Null(LgfBracketParser.ParseSingle("{1,{2,3}"));
    }

    [Fact]
    public void Dictionary_ResolvesNamesByKindAndCode()
    {
        using var fixture = Fixture.Create();
        var dictionary = LgfDictionary.Load(fixture.Location.MainFile);
        Assert.Equal("Иванов", dictionary.Resolve(LgfDictionary.UserKind, 1));
        Assert.Equal("WS-01", dictionary.Resolve(LgfDictionary.ComputerKind, 1));
        Assert.Equal("_$Data$_.Update", dictionary.Resolve(LgfDictionary.EventKind, 2));
        Assert.Equal("Справочник.Контрагенты", dictionary.Resolve(LgfDictionary.MetadataKind, 1));
        Assert.Equal("#99", dictionary.Resolve(LgfDictionary.UserKind, 99));
        Assert.Equal(string.Empty, dictionary.Resolve(LgfDictionary.UserKind, null));
    }

    [Fact]
    public void Locator_DetectsSequentialFormat()
    {
        using var fixture = Fixture.Create();
        Assert.True(fixture.Location.IsValid);
        Assert.Equal(LgdFormat.Sequential, fixture.Location.Format);
        Assert.Equal(2, fixture.Location.DataFiles.Count);
    }

    [Fact]
    public void ReadPage_ReturnsAllRecordsInFileOrder()
    {
        using var fixture = Fixture.Create();
        var entries = new SequentialLgfReader().ReadPage(fixture.Location, 0, 100).ToList();
        Assert.Equal(4, entries.Count);
        Assert.Equal(new DateTime(2026, 10, 1, 9, 0, 0), entries[0].Timestamp);
        Assert.Equal("Иванов", entries[0].User);
        Assert.Equal("WS-01", entries[0].Computer);
        Assert.Equal("1CV8C", entries[0].Application);
        Assert.Equal("_$Session$_.Start", entries[0].EventName);
        Assert.Equal(EventLogSeverity.Info, entries[0].Severity);
        Assert.Equal(7, entries[0].Session);

        var error = entries[1];
        Assert.Equal(EventLogSeverity.Error, error.Severity);
        Assert.Equal("Ошибка \"проведения\"", error.Comment);
        Assert.Equal("Справочник.Контрагенты", error.MetadataName);
        Assert.Equal("Справочник", error.MetadataType);
        Assert.Equal(EventLogTransactionStatus.RolledBack, error.TransactionStatus);
        Assert.Equal("ООО Ромашка", error.DataPresentation);
    }

    [Fact]
    public void CountRows_CountsAllFragments()
    {
        using var fixture = Fixture.Create();
        Assert.Equal(4, new SequentialLgfReader().CountRows(fixture.Location));
    }

    [Fact]
    public void ReadNewestFirst_ReturnsNewestFirstWithinPeriod()
    {
        using var fixture = Fixture.Create();
        var entries = new SequentialLgfReader()
            .ReadNewestFirst(fixture.Location, new DateTime(2026, 10, 1), DateTime.MaxValue)
            .ToList();
        Assert.Equal(4, entries.Count);
        Assert.True(entries[0].Timestamp >= entries[1].Timestamp);
        Assert.Equal(new DateTime(2026, 10, 2, 8, 30, 0), entries[0].Timestamp);
    }

    [Fact]
    public void SelectFragments_SkipsFragmentsOutsidePeriod()
    {
        var files = new[] { "a/20261001000000.lgp", "a/20261002000000.lgp", "a/20261003000000.lgp" };
        var selected = SequentialLgfReader.SelectFragments(files,
            new DateTime(2026, 10, 2, 12, 0, 0), new DateTime(2026, 10, 2, 13, 0, 0));
        Assert.Single(selected);
        Assert.EndsWith("20261002000000.lgp", selected[0]);

        var all = SequentialLgfReader.SelectFragments(files, DateTime.MinValue, DateTime.MaxValue);
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public void Query_FiltersBySeverityAndText()
    {
        using var fixture = Fixture.Create();
        var query = new EventLogQuery
        {
            From = DateTime.MinValue,
            To = DateTime.MaxValue,
            IncludeInfo = false,
            IncludeNote = false,
            IncludeWarning = false,
            IncludeError = true
        };
        var result = EventLogQueryService.Query(fixture.Location, query);
        Assert.Null(result.Error);
        Assert.Single(result.Entries);
        Assert.Equal(EventLogSeverity.Error, result.Entries[0].Severity);

        var byText = EventLogQueryService.Query(fixture.Location, new EventLogQuery
        {
            From = DateTime.MinValue,
            Text = "ромашка"
        });
        Assert.Single(byText.Entries);
    }

    [Fact]
    public void Query_RespectsMaxRows()
    {
        using var fixture = Fixture.Create();
        var result = EventLogQueryService.Query(fixture.Location, new EventLogQuery
        {
            From = DateTime.MinValue,
            MaxRows = 2
        });
        Assert.Equal(2, result.Entries.Count);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void Query_InvalidLocation_ReturnsError()
    {
        var result = EventLogQueryService.Query(new JournalLocation { ErrorMessage = "нет" }, new EventLogQuery());
        Assert.Equal("нет", result.Error);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public void CountErrors_ReturnsNumberOfErrors()
    {
        using var fixture = Fixture.Create();
        Assert.Equal(1, EventLogQueryService.CountErrors(fixture.Location, DateTime.MinValue, DateTime.MaxValue));
        Assert.Null(EventLogQueryService.CountErrors(new JournalLocation(), DateTime.MinValue, DateTime.MaxValue));
    }

    [Theory]
    [InlineData("_$Session$_.Start", "Session.Start")]
    [InlineData("$Data.Update", "Data.Update")]
    [InlineData("Custom.Event", "Custom.Event")]
    [InlineData("", "")]
    public void Presentation_NormalizesEventNames(string raw, string expected)
    {
        Assert.Equal(expected, EventLogPresentation.NormalizeEventName(raw));
    }

    [Fact]
    public void Csv_EscapesSeparatorsAndQuotes()
    {
        Assert.Equal("plain", EventLogCsvExporter.Escape("plain"));
        Assert.Equal("\"a;b\"", EventLogCsvExporter.Escape("a;b"));
        Assert.Equal("\"say \"\"hi\"\"\"", EventLogCsvExporter.Escape("say \"hi\""));
    }

    [Fact]
    public void Csv_ExportWritesHeaderAndRows()
    {
        using var fixture = Fixture.Create();
        var entries = new SequentialLgfReader().ReadPage(fixture.Location, 0, 100).ToList();
        var path = Path.Combine(fixture.Dir, "out.csv");
        EventLogCsvExporter.Export(path, entries);
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        Assert.Equal(5, lines.Length);
    }

    [Fact]
    public void ResolvePeriod_Presets()
    {
        var now = new DateTime(2026, 10, 23, 15, 30, 0);
        Assert.Equal(now.AddHours(-1), EventLogViewModel.ResolvePeriod(0, now).From);
        Assert.Equal(now.Date, EventLogViewModel.ResolvePeriod(1, now).From);
        var yesterday = EventLogViewModel.ResolvePeriod(2, now);
        Assert.Equal(now.Date.AddDays(-1), yesterday.From);
        Assert.True(yesterday.To < now.Date);
        Assert.Equal(now.Date.AddDays(-6), EventLogViewModel.ResolvePeriod(3, now).From);
        Assert.Equal(DateTime.MinValue, EventLogViewModel.ResolvePeriod(5, now).From);
    }

    [Fact]
    public void SqliteToPlatformDate_RoundTrips()
    {
        var value = new DateTime(2026, 10, 23, 10, 0, 0);
        Assert.Equal(value, SqliteLgdReader.FromPlatformDate(SqliteLgdReader.ToPlatformDate(value)));
    }

    private sealed class Fixture : IDisposable
    {
        public string Dir { get; }
        public JournalLocation Location { get; }

        private Fixture(string dir)
        {
            Dir = dir;
            Location = LgdJournalLocator.FromDirectory(dir);
        }

        public static Fixture Create()
        {
            var dir = Path.Combine(Path.GetTempPath(), $"lgf-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "1Cv8.lgf"), LgfContent, new UTF8Encoding(true));

            var header = "1CV8LOG(ver 2.0)\r\nf2a1b3c4-0000-0000-0000-000000000000\r\n\r\n";
            File.WriteAllText(Path.Combine(dir, "20261001000000.lgp"), header +
                Record("20261001090000", "I", 1, "") + ",\r\n" +
                Record("20261001091500", "E", 2, "Ошибка \"проведения\"", 1, "R", "ООО Ромашка"),
                new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(dir, "20261002000000.lgp"), header +
                Record("20261002080000", "W", 2, "Предупреждение") + ",\r\n" +
                Record("20261002083000", "I", 1, "Вход"),
                new UTF8Encoding(true));
            return new Fixture(dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(Dir, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
