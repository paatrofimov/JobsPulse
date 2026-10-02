using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Storage.PersistentModels;

/// <summary>
/// Table `watchlist_event` - the history of changes reported to a watchlist, written with the outbox rows and kept
/// after they are purged. Deleted with its watchlist.
/// </summary>
public class PersistentWatchlistEvent
{
    public long Id { get; set; }

    public long WatchlistId { get; set; }

    public required string SourceId { get; set; }
    public required string BoardId { get; set; }
    public required string PostId { get; set; }

    public required string CompanyName { get; set; }

    public string? Location { get; set; }

    /// <summary>Stored as int, like `outbox.change_kind`.</summary>
    public VacancyChangeKind ChangeKind { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>The `traversal_run` of the job that committed it; no FK - those rows are dropped after a day.</summary>
    public long? RunId { get; set; }

    public PersistentWatchlist? Watchlist { get; set; }
}
