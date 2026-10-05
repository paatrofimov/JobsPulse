namespace JobsPulse.Core.Model.Infrastructure;

/// <summary>
/// One scheduled digest of a watchlist: the period it covered. The next digest starts where the last delivered one
/// ended, and the «all changes» button of a digest reopens exactly its period.
/// </summary>
public sealed record WatchlistDigest
{
    public required long Id { get; init; }

    public required long WatchlistId { get; init; }

    public required DateTimeOffset From { get; init; }

    public required DateTimeOffset To { get; init; }

    /// <summary>The digest this one continues; null for the first one, which covers <c>Digest:PeriodDays</c>.</summary>
    public long? PreviousId { get; init; }

    /// <summary>Null until the message is sent - an undelivered digest does not move the start of the next one.</summary>
    public DateTimeOffset? DeliveredAt { get; init; }
}
