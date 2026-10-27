using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты разбора новых CLI-команд п.6 «MCP-сервер и расширение CLI» (выпуск 1.0.0.88):
/// <c>--mcp</c>, <c>--update</c> (с --apply), <c>--eventlog</c> (фильтры и цель),
/// <c>--check</c> и валидация обязательных параметров. Чистый парсер <see cref="CliArgs.Parse"/>
/// без обращения к DI/процессам.
/// </summary>
public sealed class CliMcpTests
{
    // ------------------------------------------------------------------ --mcp

    [Fact]
    public void Parse_Mcp_ReturnsMcpKind()
    {
        var result = CliArgs.Parse(new[] { "--mcp" });

        Assert.NotNull(result);
        Assert.Equal(CliCommandKind.Mcp, result.Kind);
    }

    [Fact]
    public void Parse_McpWithProfile_ReturnsBoth()
    {
        var result = CliArgs.Parse(new[] { "--mcp", "--profile", "p-1" });

        Assert.NotNull(result);
        Assert.Equal(CliCommandKind.Mcp, result.Kind);
        Assert.Equal("p-1", result.Options.Profile);
    }

    // ---------------------------------------------------------------- --update

    [Fact]
    public void Parse_Update_ReturnsUpdateKind()
    {
        var result = CliArgs.Parse(new[] { "--update" });

        Assert.NotNull(result);
        Assert.Equal(CliCommandKind.Update, result.Kind);
        Assert.False(result.Options.Apply);
    }

    [Fact]
    public void Parse_UpdateWithApply_SetsApply()
    {
        var result = CliArgs.Parse(new[] { "--update", "--apply", "--json" });

        Assert.NotNull(result);
        Assert.Equal(CliCommandKind.Update, result.Kind);
        Assert.True(result.Options.Apply);
        Assert.True(result.Options.Json);
    }

    [Fact]
    public void Parse_UpdateEqualsForm_Works()
    {
        var result = CliArgs.Parse(new[] { "--update", "--apply=true" });

        Assert.NotNull(result);
        Assert.Equal(CliCommandKind.Update, result.Kind);
        Assert.True(result.Options.Apply);
    }

    [Fact]
    public void Parse_UpdateWithoutApply_NoDownload()
    {
        var result = CliArgs.Parse(new[] { "--update" });

        Assert.NotNull(result);
        Assert.False(result.Options.Apply);
    }

    // -------------------------------------------------------------- --eventlog

    [Fact]
    public void Parse_Eventlog_WithTargetAndFilters()
    {
        var result = CliArgs.Parse(new[]
        {
            "--eventlog", "--target", "Бухгалтерия",
            "--from", "2026-10-01", "--to", "2026-10-31",
            "--user", "Иванов", "--event", "Data.Update",
            "--limit", "100", "--json"
        });

        Assert.NotNull(result);
        Assert.Equal(CliCommandKind.Eventlog, result.Kind);
        Assert.Equal("Бухгалтерия", result.Target);
        Assert.Equal("2026-10-01", result.Options.From);
        Assert.Equal("2026-10-31", result.Options.To);
        Assert.Equal("Иванов", result.Options.User);
        Assert.Equal("Data.Update", result.Options.Event);
        Assert.Equal(100, result.Options.Limit);
        Assert.True(result.Options.Json);
    }

    [Fact]
    public void Parse_Eventlog_EqualsForms()
    {
        var result = CliArgs.Parse(new[] { "--eventlog", "--target=C:\\ib\\base.1CD", "--limit=50" });

        Assert.NotNull(result);
        Assert.Equal(CliCommandKind.Eventlog, result.Kind);
        Assert.Equal(@"C:\ib\base.1CD", result.Target);
        Assert.Equal(50, result.Options.Limit);
    }

    [Fact]
    public void Parse_Eventlog_WithoutTarget_ReturnsError()
    {
        var result = CliArgs.Parse(new[] { "--eventlog" });

        Assert.NotNull(result);
        Assert.Equal(CliCommandKind.Eventlog, result.Kind);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Parse_Eventlog_TextFilter_Reads()
    {
        var result = CliArgs.Parse(new[] { "--eventlog", "--target", "База", "--text", "ошибка" });

        Assert.NotNull(result);
        Assert.Equal("ошибка", result.Options.Text);
    }

    // ----------------------------------------------------------------- --check

    [Fact]
    public void Parse_Check_ReturnsCheckKind()
    {
        var result = CliArgs.Parse(new[] { "--check" });

        Assert.NotNull(result);
        Assert.Equal(CliCommandKind.Check, result.Kind);
    }

    [Fact]
    public void Parse_CheckWithJson_SetsJson()
    {
        var result = CliArgs.Parse(new[] { "--check", "--json" });

        Assert.NotNull(result);
        Assert.Equal(CliCommandKind.Check, result.Kind);
        Assert.True(result.Options.Json);
    }

    // --------------------------------------------------- перекрытие существующего

    [Fact]
    public void Parse_McpTakesPriorityOverRun()
    {
        // Смешанный запуск: --mcp имеет приоритет (парсер фиксирует последнюю команду).
        var result = CliArgs.Parse(new[] { "--run", "База", "--mcp" });

        Assert.NotNull(result);
        Assert.Equal(CliCommandKind.Mcp, result.Kind);
    }
}