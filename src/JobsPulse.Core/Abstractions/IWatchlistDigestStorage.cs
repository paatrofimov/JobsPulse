using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Core.Abstractions;

/// <summary>The scheduled digests sent to each watchlist (`watchlist_digest`).</summary>
public interface IWatchlistDigestStorage
{
    Task<WatchlistDigest?> GetAsync(long id, CancellationToken ct);

    /// <summary>The latest digest of a watchlist that reached its chat.</summary>
    Task<WatchlistDigest?> GetLastDeliveredAsync(long watchlistId, CancellationToken ct);

    /// <summary>Stored before sending: the message carries its id in the «all changes» button.</summary>
    Task<WatchlistDigest> AddAsync(
        long watchlistId,
        DateTimeOffset from,
        DateTimeOffset to,
        long? previousId,
        CancellationToken ct);

    Task MarkDeliveredAsync(long id, DateTimeOffset deliveredAt, CancellationToken ct);
}
