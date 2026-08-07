namespace JobsPulse.Core.Model.Infrastructure;

public sealed record DeliveryResult
{
    public required bool Success { get; init; }
    public string? Error { get; init; }

    // Telegram 429 retry_after
    public TimeSpan? RetryAfter { get; init; }

    public static readonly DeliveryResult Ok = new() { Success = true };

    public static DeliveryResult Fail(string error, TimeSpan? retryAfter = null) =>
        new() { Success = false, Error = error, RetryAfter = retryAfter };
}