namespace JobsPulse.Core.Abstractions;

/// <summary>
/// Приёмник уведомлений. Сейчас Telegram, потом может быть почта/вебхук/что угодно.
/// Батч приходит уже сгруппированным по чату — sink отвечает только за формат и отправку.
/// </summary>
public interface IVacancySink
{
    string SinkId { get; }

    Task<DeliveryResult> DeliverAsync(
        string chatId,
        IReadOnlyList<OutboxItem> batch,
        CancellationToken ct);
}

public sealed record DeliveryResult
{
    public required bool Success { get; init; }
    public string? Error { get; init; }

    /// <summary>Приёмник попросил притормозить (Telegram 429 retry_after).</summary>
    public TimeSpan? RetryAfter { get; init; }

    public static readonly DeliveryResult Ok = new() { Success = true };

    public static DeliveryResult Fail(string error, TimeSpan? retryAfter = null) =>
        new() { Success = false, Error = error, RetryAfter = retryAfter };
}
