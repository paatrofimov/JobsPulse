namespace JobsPulse.Storage.PersistentModels;

/// <summary>Table `watchlist` - the configuration itself, one row per named set of boards.</summary>
public class PersistentWatchlist
{
    public long Id { get; set; }

    public required string Name { get; set; }

    /// <summary>Telegram user id of the owner. Null for a watchlist created before ownership existed.</summary>
    public long? OwnerUserId { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>`FilterSpec` as jsonb - the filter is read and written whole, never queried by field.</summary>
    public required string Filter { get; set; }

    public int? IntervalMinutesOverride { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public List<PersistentWatchlistEntry> Entries { get; set; } = [];
}
