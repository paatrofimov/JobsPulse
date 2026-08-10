namespace JobsPulse.Core.Model.Infrastructure;

/// <summary>
/// The match layer: «this vacancy passed the filter of this watchlist, and this is the content that was reported».
/// Separate from <c>seen_vacancy</c>, which stays the global state of the ATS vacancy itself.
/// </summary>
public sealed record WatchlistMatch
{
    public required long WatchlistId { get; init; }

    public required string SourceId { get; init; }

    public required string BoardId { get; init; }

    public required string PostId { get; init; }

    /// <summary>Content hash last reported to this watchlist - the basis of the Updated change.</summary>
    public required string ContentHash { get; init; }

    public required string FilterHash { get; init; }
}
