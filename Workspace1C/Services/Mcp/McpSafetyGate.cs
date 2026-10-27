using System;
using System.Threading;
using System.Threading.Tasks;

namespace Workspace1C.Services.Mcp;

/// <summary>Исход запроса подтверждения опасной операции MCP-инструмента.</summary>
public enum McpConfirmOutcome
{
    /// <summary>Подтверждено (нажата кнопка «Да»). Операцию можно выполнять.</summary>
    Approved,

    /// <summary>Отклонено (кнопка «Нет»). Операция НЕ выполняется.</summary>
    Denied,

    /// <summary>Время ожидания истекло. Операция НЕ выполняется.</summary>
    Timeout,

    /// <summary>Telegram не настроен (токен/чат пустые) — подтверждение получить нельзя.</summary>
    NotConfigured,

    /// <summary>Ошибка отправки/поллинга (сеть, занятость, внутренняя).</summary>
    Error
}

/// <summary>Результат запроса подтверждения опасной операции.</summary>
public sealed class McpConfirmation
{
    /// <summary>Исход запроса (см. <see cref="McpConfirmOutcome"/>).</summary>
    public McpConfirmOutcome Outcome { get; init; }

    /// <summary>Машинное имя исхода для текстового ответа инструмента (yes/no/timeout/…).</summary>
    public string MachineName { get; init; } = string.Empty;

    /// <summary>Можно ли выполнять операцию (только при Approved).</summary>
    public bool Approved => Outcome == McpConfirmOutcome.Approved;

    /// <summary>Человекочитаемое пояснение (сообщение ошибки/статус).</summary>
    public string Detail { get; init; } = string.Empty;

    public static McpConfirmation From(TelegramConfirmationResult? result, string? error)
    {
        if (result is null)
        {
            var notConfigured = string.Equals(error, "not_configured", StringComparison.OrdinalIgnoreCase);
            return new McpConfirmation
            {
                Outcome = notConfigured ? McpConfirmOutcome.NotConfigured : McpConfirmOutcome.Error,
                MachineName = notConfigured ? "not_configured" : "error",
                Detail = error ?? string.Empty
            };
        }

        return new McpConfirmation
        {
            Outcome = result.Outcome switch
            {
                TelegramConfirmationOutcome.Yes => McpConfirmOutcome.Approved,
                TelegramConfirmationOutcome.No => McpConfirmOutcome.Denied,
                _ => McpConfirmOutcome.Timeout
            },
            MachineName = result.MachineName,
            Detail = result.Text
        };
    }
}

/// <summary>
/// Ворота безопасности опасных операций MCP-инструментов (п.6 «MCP-сервер и расширение
/// CLI», выпуск 1.0.0.88). Инструмент, помеченный <c>RequiresConfirmation</c>, перед
/// выполнением вызывает <see cref="RequireApprovalAsync"/>: запрос уходит через уже
/// реализованный <see cref="TelegramConfirmationService"/> (inline-кнопки «Да»/«Нет»,
/// выпуск 1.0.0.36), и без исхода <see cref="McpConfirmOutcome.Approved"/> действие
/// НЕ выполняется. Результат подтверждения включается в ответ инструмента.
/// <para>
/// В базовом наборе инструментов 1.0.0.88 опасных нет (list/launch/clone/eventlog/
/// sessions/jobs — чтение и безопасные действия); обёртка используется расширенными
/// инструментами вроде <c>terminate_session</c> (завершение сеанса сервера 1С) и
/// подключается к будущим операциям (удаление базы, изменение расписаний).
/// </para>
/// </summary>
public class McpSafetyGate
{
    /// <summary>Таймаут ожидания подтверждения по умолчанию (совпадает с CLI, 300 с).</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

    private readonly TelegramConfirmationService? _confirmationService;

    /// <param name="confirmationService">
    /// Сервис подтверждения через inline-кнопки Telegram. null — инструмент помечается
    /// «not_configured» (без Telegram опасные операции не выполняются).
    /// </param>
    public McpSafetyGate(TelegramConfirmationService? confirmationService)
    {
        _confirmationService = confirmationService;
    }

    /// <summary>
    /// Запрашивает подтверждение опасной операции. Возвращает исход запроса; выполнять
    /// операцию можно ТОЛЬКО при <see cref="McpConfirmation.Approved"/>.
    /// </summary>
    public async Task<McpConfirmation> RequireApprovalAsync(
        string prompt, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        // Запрос через реальный TelegramConfirmationService (или заглушку в юнит-тестах).
        return await ConfirmImplAsync(prompt, timeout ?? DefaultTimeout, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Точка расширения для юнит-тестов: подменяет реальный вызов
    /// <see cref="TelegramConfirmationService.ConfirmAsync"/>.
    /// </summary>
    protected internal virtual Task<McpConfirmation> ConfirmImplAsync(
        string prompt, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (_confirmationService is null)
        {
            return Task.FromResult(new McpConfirmation
            {
                Outcome = McpConfirmOutcome.NotConfigured,
                MachineName = "not_configured",
                Detail = "Telegram не настроен: опасные операции MCP требуют подтверждения через Telegram."
            });
        }

        // Реальный вызов: (Ok, Error, Result) → McpConfirmation. Ошибка Ok=false (busy,
        // not_configured, сеть) превращается в NotConfigured/Error — операция не выполняется.
        return InvokeAsync(prompt, timeout, cancellationToken);
    }

    private async Task<McpConfirmation> InvokeAsync(string prompt, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var (ok, error, result) = await _confirmationService!
            .ConfirmAsync(prompt, null, timeout, cancellationToken)
            .ConfigureAwait(false);
        return ok
            ? McpConfirmation.From(result, null)
            : McpConfirmation.From(null, error);
    }
}