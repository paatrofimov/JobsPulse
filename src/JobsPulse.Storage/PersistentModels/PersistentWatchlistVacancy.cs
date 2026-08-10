namespace JobsPulse.Storage.PersistentModels;

/// <summary>
/// Table `watchlist_vacancy` - the match layer between a watchlist and a vacancy: «this post passed this filter and
/// this content was reported». Derived state: rows are deleted as soon as the match is gone, history stays in
/// `outbox`. The ATS vacancy itself lives in `seen_vacancy` and is not duplicated here.
/// </summary>
public class PersistentWatchlistVacancy
{
    public long Id { get; set; }

    public long WatchlistId { get; set; }

    public required string SourceId { get; set; }
    public required string BoardId { get; set; }
    public required string PostId { get; set; }

    /// <summary>Content hash last reported to this watchlist - the basis of the Updated change.</summary>
    public required string ContentHash { get; set; }

    /// <summary>Filter the post passed, for diagnostics of a stale match.</summary>
    public string? FilterHash { get; set; }

    public DateTimeOffset MatchedAt { get; set; }

    public PersistentWatchlist? Watchlist { get; set; }
}
