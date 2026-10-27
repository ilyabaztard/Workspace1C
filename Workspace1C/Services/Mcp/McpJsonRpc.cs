using System.Text.Json;
using System.Text.Json.Nodes;

namespace Workspace1C.Services.Mcp;

/// <summary>
/// Модели JSON-RPC 2.0 каркаса MCP-сервера (п.6 «MCP-сервер и расширение CLI»,
/// выпуск 1.0.0.88). Чистые данные без транспорта: запрос (строка stdin),
/// ответ успеха/ошибки (строка stdout) и хелперы сериализации.
/// <para>
/// Каждое сообщение stdio — ОДНА строка JSON (как требует MCP over stdio).
/// id запроса сохраняется «как есть» (number или string), чтобы ответ можно
/// было сопоставить клиентом; уведомления (без id) ответа не требуют.
/// </para>
/// </summary>
public sealed class McpJsonRpc
{
    /// <summary>Версия протокола JSON-RPC, фиксированная для всех сообщений.</summary>
    public const string JsonRpcVersion = "2.0";

    /// <summary>Версия MCP-протокола, объявляемая в ответе initialize.</summary>
    public const string ProtocolVersion = "2024-11-05";

    /// <summary>Имя сервера в ответе initialize.</summary>
    public const string ServerName = "Workspace 1C";

    /// <summary>Опции сериализации: компактный однострочный JSON, кириллица читаемая UTF-8.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // Однострочный JSON для stdio: каждая строка — одно сообщение.
        WriteIndented = false,
        // Кириллицу и прочие не-ASCII пишем читаемыми UTF-8, а не \uXXXX
        // (паттерн CliOutput.SerializeJson / InfobaseJsonTransfer.Serialize).
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Разобранное сообщение запроса из строки stdin.</summary>
    public sealed class McpRequest
    {
        /// <summary>Признак «есть id» (запрос, а не уведомление).</summary>
        public bool HasId { get; init; }

        /// <summary>Сырое значение id (number/string), повторяется в ответе как есть.</summary>
        public JsonNode? Id { get; init; }

        /// <summary>Имя метода (initialize, tools/list, tools/call, ping, …).</summary>
        public string? Method { get; init; }

        /// <summary>Параметры метода (объект) или null.</summary>
        public JsonObject? Params { get; init; }
    }

    /// <summary>Результат разбора строки: команда обработки.</summary>
    public enum ParseOutcome
    {
        /// <summary>Пустая/пробельная строка — пропустить без ответа.</summary>
        Empty,

        /// <summary>Запрос/уведомление готовы к обработке.</summary>
        Ok,

        /// <summary>Невалидный JSON — ответ ошибкой ParseError (id неизвестен → null).</summary>
        InvalidJson,

        /// <summary>Валидный JSON, но не объект запроса (массив, строка, число) — InvalidRequest.</summary>
        NotAnObject
    }

    /// <summary>
    /// Разбирает строку stdin в запрос JSON-RPC. Возвращает null при ошибке разбора —
    /// вызывающий отвечает ошибкой <see cref="McpProtocolError.ParseError"/>/<see cref="McpProtocolError.InvalidRequest"/>.
    /// </summary>
    public static (ParseOutcome Outcome, McpRequest? Request) Parse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return (ParseOutcome.Empty, null);

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(line);
        }
        catch (JsonException)
        {
            return (ParseOutcome.InvalidJson, null);
        }

        if (node is not JsonObject obj)
            return (ParseOutcome.NotAnObject, null);

        bool hasId = obj.TryGetPropertyValue("id", out var idNode);
        if (hasId && idNode is not null && idNode.GetValueKind() is not (JsonValueKind.Number or JsonValueKind.String))
            return (ParseOutcome.NotAnObject, null);

        var method = obj.TryGetPropertyValue("method", out var methodNode) ? methodNode?.GetValue<string>() : null;

        JsonObject? parameters = null;
        if (obj.TryGetPropertyValue("params", out var paramsNode) && paramsNode is JsonObject objParams)
            parameters = objParams;

        return (ParseOutcome.Ok, new McpRequest { HasId = hasId, Id = idNode, Method = method, Params = parameters });
    }

    /// <summary>Сериализует ответ успеха: {"jsonrpc":"2.0","id":…,"result":…}.</summary>
    public static string SerializeResult(JsonNode? id, object? result) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["jsonrpc"] = JsonRpcVersion,
            ["id"] = id is null ? null : JsonSerializer.SerializeToNode(id),
            ["result"] = result
        }, JsonOptions);

    /// <summary>Сериализует ответ ошибки: {"jsonrpc":"2.0","id":…,"error":{code,message,data?}}.</summary>
    public static string SerializeError(JsonNode? id, int code, string message, object? data = null)
    {
        var error = new Dictionary<string, object?>
        {
            ["code"] = code,
            ["message"] = message,
        };
        if (data is not null)
            error["data"] = data;

        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["jsonrpc"] = JsonRpcVersion,
            ["id"] = id is null ? null : JsonSerializer.SerializeToNode(id),
            ["error"] = error
        }, JsonOptions);
    }

    /// <summary>Читает строковое свойство параметров (null — отсутствует/не строка).</summary>
    public static string? GetString(JsonObject? parameters, string name)
    {
        if (parameters is null || !parameters.TryGetPropertyValue(name, out var value) || value is null)
            return null;
        return value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : value.ToString();
    }

    /// <summary>Читает булево свойство параметров (null/false — не задано/ложь).</summary>
    public static bool GetBool(JsonObject? parameters, string name, bool defaultValue = false)
    {
        if (parameters is null || !parameters.TryGetPropertyValue(name, out var value) || value is null)
            return defaultValue;
        return value.GetValueKind() == JsonValueKind.True;
    }

    /// <summary>Читает целочисленное свойство (null — не задано).</summary>
    public static int? GetInt(JsonObject? parameters, string name)
    {
        if (parameters is null || !parameters.TryGetPropertyValue(name, out var value) || value is null)
            return null;
        if (value.GetValueKind() != JsonValueKind.Number)
            return null;
        try { return value.GetValue<int>(); }
        catch { return null; }
    }
}