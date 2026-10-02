using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Core.Abstractions;

/// <summary>
/// The history of changes reported to each watchlist (`watchlist_event`). Written by
/// <see cref="IStateStore.CommitAsync"/> together with the outbox rows, read by the period statistics.
/// </summary>
public interface IWatchlistEventStorage
{
    /// <summary>Every event of a watchlist that occurred up to <paramref name="until"/> inclusive, oldest first.</summary>
    Task<IReadOnlyList<WatchlistEvent>> LoadAsync(long watchlistId, DateTimeOffset until, CancellationToken ct);

    /// <summary>Appends events outside a commit - the restored history of <c>WatchlistHistoryRepair</c>.</summary>
    Task<int> AppendAsync(IReadOnlyList<WatchlistEvent> events, CancellationToken ct);
}
