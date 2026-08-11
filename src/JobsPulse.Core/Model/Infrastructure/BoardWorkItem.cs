namespace JobsPulse.Core.Model.Infrastructure;

/// <summary>
/// One unit of polling work: a board plus every watchlist interested in it. One board is never fetched twice in a
/// cycle, no matter how many watchlists share it.
/// </summary>
public sealed record BoardWorkItem
{
    public required string SourceId { get; init; }

    public required string BoardId { get; init; }

    /// <summary>Only for logs - the reported company name comes from the subscription.</summary>
    public required string CompanyName { get; init; }

    /// <summary>Source-specific board parameters as json - see <see cref="BoardCandidate.Configuration"/>.</summary>
    public string? Configuration { get; init; }

    /// <summary>Empty for registry boards: nothing watches them, so they produce no notifications.</summary>
    public IReadOnlyList<WatchlistSubscription> Subscriptions { get; init; } = [];

    /// <summary>Smallest override among the subscribed watchlists.</summary>
    public int? IntervalMinutesOverride { get; init; }

    public string BoardKey => $"{SourceId}/{BoardId}";
}
