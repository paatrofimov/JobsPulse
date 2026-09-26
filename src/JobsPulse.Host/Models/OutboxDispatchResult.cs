namespace JobsPulse.Host.Models;

public readonly record struct OutboxDispatchResult(int Delivered, TimeSpan? RetryAfter)
{
    public static readonly OutboxDispatchResult Idle = new(0, null);

    public bool Failed => RetryAfter.HasValue;
}
