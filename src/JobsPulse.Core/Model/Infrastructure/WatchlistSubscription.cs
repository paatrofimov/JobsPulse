namespace JobsPulse.Core.Model.Infrastructure;

/// <summary>
/// One watchlist's interest in one board: which filter to apply and under which name to report the result.
/// A board is fetched once and then evaluated against every subscription of it.
/// </summary>
public sealed record WatchlistSubscription
{
    public required long WatchlistId { get; init; }

    public required string WatchlistName { get; init; }

    public required string CompanyName { get; init; }

    public required FilterSpec Filter { get; init; }

    public required string FilterHash { get; init; }
}
