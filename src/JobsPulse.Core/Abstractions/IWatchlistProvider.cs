using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Core.Abstractions;

public interface IWatchlistProvider
{
    // Returns latest valid most actual watchlist
    Watchlist Current { get; }

    Task<WatchEntry> AddAsync(WatchEntry entry, CancellationToken ct);

    Task<bool> RemoveAsync(string entryId, CancellationToken ct);

    Task<bool> SetEnabledAsync(string entryId, bool enabled, CancellationToken ct);
}