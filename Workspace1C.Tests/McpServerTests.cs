using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Services;
using Workspace1C.Services.Mcp;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты MCP-сервера (п.6 «MCP-сервер и расширение CLI», выпуск 1.0.0.88):
/// JSON-RPC 2.0 цикл (initialize, ping, notifications), tools/list (состав и схемы
/// инструментов), tools/call (правильный, с ошибкой инструмента, не найден) и
/// поведение McpSafetyGate (подтверждено/отклонено/таймаут/не настроено).
/// В тестах НЕ настраивается DI и не выполняются внешние операции: проверяются
/// только чистые части (разбор, диспетчеризация, ворота безопасности).
/// </summary>
public sealed class McpServerTests
{
    /// <summary>Заглушка ворот безопасности с заданным исходом подтверждения.</summary>
    private sealed class StubSafetyGate : McpSafetyGate
    {
        private readonly McpConfirmation _confirmation;

        public StubSafetyGate(McpConfirmation confirmation) : base(confirmationService: null)
        {
            _confirmation = confirmation;
        }

        protected internal override Task<McpConfirmation> ConfirmImplAsync(
            string prompt, System.TimeSpan timeout, CancellationToken cancellationToken)
            => Task.FromResult(_confirmation);
    }

    private static McpConfirmation Outcome(McpConfirmOutcome outcome) => new()
    {
        Outcome = outcome,
        MachineName = outcome switch
        {
            McpConfirmOutcome.Approved => "yes",
            McpConfirmOutcome.Denied => "no",
            McpConfirmOutcome.Timeout => "timeout",
            McpConfirmOutcome.NotConfigured => "not_configured",
            _ => "error"
        }
    };

    private static McpServer CreateServer(McpSafetyGate? gate = null) => new(
        output: new System.IO.StringWriter(),
        diagnostics: new System.IO.StringWriter(),
        safetyGate: gate ?? new McpSafetyGate(confirmationService: null));

    // ----------------------------------------------------------- initialize / ping

    [Fact]
    public void Initialize_ReturnsProtocolAndServerInfo()
    {
        var server = CreateServer();
        var response = server.HandleMessage(
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"test"}}}""");

        Assert.NotNull(response);
        var doc = JsonNode.Parse(response!);
        Assert.NotNull(doc);
        Assert.Equal("2.0", (string?)doc!["jsonrpc"]);
        Assert.Equal(1, (int?)doc["id"]);
        var result = doc["result"]!;
        Assert.Equal("2024-11-05", (string?)result["protocolVersion"]);
        Assert.Equal("Workspace 1C", (string?)result["serverInfo"]!["name"]);
        Assert.NotNull(result["capabilities"]!["tools"]);
    }

    [Fact]
    public void Ping_ReturnsEmptyResult()
    {
        var server = CreateServer();
        var response = server.HandleMessage("""{"jsonrpc":"2.0","id":"abc","method":"ping"}""");

        Assert.NotNull(response);
        var doc = JsonNode.Parse(response!);
        Assert.Equal("abc", (string?)doc!["id"]);
        Assert.Equal(JsonValueKind.Object, doc["result"]!.GetValueKind());
    }

    [Fact]
    public void InitializedNotification_ResultsInNoResponse()
    {
        var server = CreateServer();
        // Уведомление (без id) — ответ не требуется.
        Assert.Null(server.HandleMessage("""{"jsonrpc":"2.0","method":"notifications/initialized"}"""));
    }

    // ---------------------------------------------------------------- tools/list

    [Fact]
    public void ToolsList_ContainsAllBaseToolsWithSchemas()
    {
        var server = CreateServer();
        var response = server.HandleMessage("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");

        Assert.NotNull(response);
        var doc = JsonNode.Parse(response!);
        var tools = (JsonArray)doc!["result"]!["tools"]!;

        var names = tools.Select(t => (string?)t!["name"]).ToList();
        Assert.Contains("list_infobases", names);
        Assert.Contains("launch_infobase", names);
        Assert.Contains("clone_infobase", names);
        Assert.Contains("event_log_query", names);
        Assert.Contains("server_sessions", names);
        Assert.Contains("scheduled_jobs_state", names);
        // Опасный инструмент с подтверждением Telegram присутствует отдельно.
        Assert.Contains("terminate_session", names);

        // У каждого инструмента есть описание и JSON Schema параметров.
        foreach (var tool in tools)
        {
            Assert.False(string.IsNullOrWhiteSpace((string?)tool["description"]));
            var schema = tool["inputSchema"];
            Assert.Equal(JsonValueKind.Object, schema!.GetValueKind());
            Assert.NotNull(schema["type"]);
            Assert.Equal(JsonValueKind.Object, schema["properties"]!.GetValueKind());
        }
    }

    [Fact]
    public void ToolsList_LaunchToolRequiresName()
    {
        var server = CreateServer();
        var response = server.HandleMessage("""{"jsonrpc":"2.0","id":3,"method":"tools/list"}""");

        var doc = JsonNode.Parse(response!);
        var tools = (JsonArray)doc!["result"]!["tools"]!;
        var launch = tools.First(t => (string?)t!["name"] == "launch_infobase")!;
        var required = (JsonArray)launch["inputSchema"]!["required"]!;

        Assert.Contains(required, r => (string?)r == "name");
    }

    // ---------------------------------------------------------------- tools/call

    [Fact]
    public void ToolCall_UnknownTool_ReturnsToolNotFound()
    {
        var server = CreateServer();
        var response = server.HandleMessage(
            """{"jsonrpc":"2.0","id":7,"method":"tools/call","params":{"name":"remove_database","arguments":{}}}""");

        Assert.NotNull(response);
        var doc = JsonNode.Parse(response!);
        Assert.Equal(-32001, (int?)doc!["error"]!["code"]);
        Assert.Contains("remove_database", (string?)doc["error"]!["message"]);
    }

    [Fact]
    public void ToolCall_WithoutName_ReturnsInvalidParams()
    {
        var server = CreateServer();
        var response = server.HandleMessage(
            """{"jsonrpc":"2.0","id":8,"method":"tools/call","params":{}}""");

        Assert.NotNull(response);
        var doc = JsonNode.Parse(response!);
        Assert.Equal(-32602, (int?)doc!["error"]!["code"]);
    }

    [Fact]
    public void InvalidJsonLine_ReturnsParseError()
    {
        var server = CreateServer();
        var response = server.HandleMessage("{это не json");

        Assert.NotNull(response);
        var doc = JsonNode.Parse(response!);
        Assert.Equal(-32700, (int?)doc!["error"]!["code"]);
    }

    [Fact]
    public void NonObjectJson_ReturnsInvalidRequest()
    {
        var server = CreateServer();
        var response = server.HandleMessage("""[1,2,3]""");

        Assert.NotNull(response);
        var doc = JsonNode.Parse(response!);
        Assert.Equal(-32600, (int?)doc!["error"]!["code"]);
    }

    [Fact]
    public void UnknownMethod_ReturnsMethodNotFound()
    {
        var server = CreateServer();
        var response = server.HandleMessage("""{"jsonrpc":"2.0","id":9,"method":"resources/list"}""");

        Assert.NotNull(response);
        var doc = JsonNode.Parse(response!);
        Assert.Equal(-32601, (int?)doc!["error"]!["code"]);
    }

    [Fact]
    public void EmptyLine_ReturnsNoResponse()
    {
        var server = CreateServer();
        Assert.Null(server.HandleMessage("   "));
        Assert.Null(server.HandleMessage(""));
    }

    // ------------------------------------------------------------ SafetyGate

    [Fact]
    public async Task SafetyGate_Approved_AllowsOperation()
    {
        var gate = new StubSafetyGate(Outcome(McpConfirmOutcome.Approved));
        var confirmation = await gate.RequireApprovalAsync("Разрешить?");

        Assert.True(confirmation.Approved);
        Assert.Equal(McpConfirmOutcome.Approved, confirmation.Outcome);
        Assert.Equal("yes", confirmation.MachineName);
    }

    [Fact]
    public async Task SafetyGate_Denied_BlocksOperation()
    {
        var gate = new StubSafetyGate(Outcome(McpConfirmOutcome.Denied));
        var confirmation = await gate.RequireApprovalAsync("Разрешить?");

        Assert.False(confirmation.Approved);
        Assert.Equal(McpConfirmOutcome.Denied, confirmation.Outcome);
    }

    [Fact]
    public async Task SafetyGate_Timeout_BlocksOperation()
    {
        var gate = new StubSafetyGate(Outcome(McpConfirmOutcome.Timeout));
        var confirmation = await gate.RequireApprovalAsync("Разрешить?");

        Assert.False(confirmation.Approved);
        Assert.Equal(McpConfirmOutcome.Timeout, confirmation.Outcome);
    }

    [Fact]
    public async Task SafetyGate_WithoutTelegram_ReturnsNotConfigured()
    {
        var gate = new McpSafetyGate(confirmationService: null);
        var confirmation = await gate.RequireApprovalAsync("Разрешить?");

        Assert.False(confirmation.Approved);
        Assert.Equal(McpConfirmOutcome.NotConfigured, confirmation.Outcome);
    }

    [Fact]
    public void TerminateSession_Denied_ReturnsToolErrorWithoutService()
    {
        // Ветка «отклонено» не обращается к DI/rac: проверяем ответ isError с текстом.
        var gate = new StubSafetyGate(Outcome(McpConfirmOutcome.Denied));
        var server = CreateServer(gate);

        var response = server.HandleMessage(
            """{"jsonrpc":"2.0","id":10,"method":"tools/call","params":{"name":"terminate_session","arguments":{"server":"srv","sessionId":"0f51c8b4-0000-0000-0000-000000000001"}}}""");

        Assert.NotNull(response);
        var doc = JsonNode.Parse(response!);
        Assert.Equal(JsonValueKind.True, doc!["result"]!["isError"]!.GetValueKind());
        Assert.Equal(true, (bool?)doc["result"]!["isError"]);
        var text = (string?)doc["result"]!["content"]![0]!["text"];
        Assert.Contains("denied", text);
    }

    [Fact]
    public void TerminateSession_WithoutSessionId_ReturnsToolError()
    {
        var server = CreateServer(new McpSafetyGate(confirmationService: null));
        var response = server.HandleMessage(
            """{"jsonrpc":"2.0","id":11,"method":"tools/call","params":{"name":"terminate_session","arguments":{"server":"srv"}}}""");

        Assert.NotNull(response);
        var doc = JsonNode.Parse(response!);
        Assert.Equal(true, (bool?)doc!["result"]!["isError"]);
    }
}