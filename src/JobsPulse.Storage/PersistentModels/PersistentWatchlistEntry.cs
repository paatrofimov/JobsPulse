using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Storage.PersistentModels;

/// <summary>Table `watchlist_entry` - one board inside one watchlist. Deleted with its watchlist.</summary>
public class PersistentWatchlistEntry
{
    public long Id { get; set; }

    public long WatchlistId { get; set; }

    public required string SourceId { get; set; }
    public required string BoardId { get; set; }

    public required string CompanyName { get; set; }

    /// <summary>Source-specific board parameters as json - Workday needs host, tenant and site.</summary>
    public string? Configuration { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>Manual or discovery. Stored as int, so reordering the enum breaks nothing.</summary>
    public BoardOrigin Origin { get; set; } = BoardOrigin.Manual;

    public DateTimeOffset CreatedAt { get; set; }

    public PersistentWatchlist? Watchlist { get; set; }
}
