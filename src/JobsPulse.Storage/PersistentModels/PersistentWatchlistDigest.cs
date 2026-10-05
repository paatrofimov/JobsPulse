namespace JobsPulse.Storage.PersistentModels;

/// <summary>Table `watchlist_digest` - one row per scheduled digest of a watchlist. Deleted with its watchlist.</summary>
public class PersistentWatchlistDigest
{
    public long Id { get; set; }

    public long WatchlistId { get; set; }

    public DateTimeOffset PeriodFrom { get; set; }

    public DateTimeOffset PeriodTo { get; set; }

    /// <summary>No FK - only a pointer back along the chain of digests.</summary>
    public long? PreviousId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? DeliveredAt { get; set; }

    public PersistentWatchlist? Watchlist { get; set; }
}
