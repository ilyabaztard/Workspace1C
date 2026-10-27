namespace Workspace1C.Services.Mcp;

/// <summary>
/// Коды ошибок JSON-RPC 2.0 (п.6 «MCP-сервер и расширение CLI», выпуск 1.0.0.88).
/// Зарезервированные коды JSON-RPC 2.0 и коды MCP-протокола (initialize, resources,
/// tools). Чистые константы — без зависимостей от транспорта.
/// </summary>
public static class McpProtocolError
{
    // ---- JSON-RPC 2.0, зарезервированные коды (не должны придумываться своими) ----

    /// <summary>Ошибка разбора JSON (невалидный JSON в строке stdin).</summary>
    public const int ParseError = -32700;

    /// <summary>Некорректный запрос: не объект, нет метода, и т.п.</summary>
    public const int InvalidRequest = -32600;

    /// <summary>Метод не найден (неизвестный JSON-RPC/MCP метод).</summary>
    public const int MethodNotFound = -32601;

    /// <summary>Невалидные параметры метода.</summary>
    public const int InvalidParams = -32602;

    /// <summary>Внутренняя ошибка выполнения.</summary>
    public const int InternalError = -32603;

    /// <summary>Ошибка сервера (стартовый диапазон): не удалось инициализировать сервис.</summary>
    public const int ServerErrorStart = -32000;

    // ---- MCP-протокол: кастомные коды инструментов (в диапазоне -32000 …) ----

    /// <summary>Инструмент (_tool) не найден.</summary>
    public const int ToolNotFound = -32001;

    /// <summary>Инструмент вызван с невалидными аргументами (нет обязательного поля, неверный тип).</summary>
    public const int ToolInvalidParams = -32002;

    /// <summary>Действие отклонено/не подтверждено (TelegramConfirmationService: «Нет»/таймаут).</summary>
    public const int ActionDenied = -32003;
}