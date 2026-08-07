namespace JobsPulse.Sinks.Telegram.Models;

public readonly record struct TelegramResult(
    bool Success,
    string? Error,
    TimeSpan? RetryAfter)
{
    public static readonly TelegramResult Ok = new(true, null, null);

    public static TelegramResult Fail(
        string error,
        TimeSpan? retryAfter = null)
        => new(false, error, retryAfter);
}