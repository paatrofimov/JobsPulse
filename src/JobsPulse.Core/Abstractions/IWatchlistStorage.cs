using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Core.Abstractions;

/// <summary>
/// The watchlist configuration itself - watchlists, their entries and their filters. Persistent and the only source
/// of truth: every runtime change goes straight to the database, nothing is kept in a config file.
/// </summary>
public interface IWatchlistStorage
{
    /// <summary>Enabled watchlists with their entries and filters - the input of every polling cycle.</summary>
    Task<IReadOnlyList<Watchlist>> GetEnabledAsync(CancellationToken ct);

    Task<IReadOnlyList<Watchlist>> GetAllAsync(CancellationToken ct);

    Task<Watchlist?> GetAsync(long watchlistId, CancellationToken ct);

    Task<Watchlist?> FindByNameAsync(string name, CancellationToken ct);

    /// <summary>Returns null when the name is already taken.</summary>
    Task<Watchlist?> CreateAsync(string name, FilterSpec filter, CancellationToken ct);

    Task<bool> DeleteAsync(long watchlistId, CancellationToken ct);

    Task<bool> SetEnabledAsync(long watchlistId, bool enabled, CancellationToken ct);

    Task<bool> SetFilterAsync(long watchlistId, FilterSpec filter, CancellationToken ct);

    Task<bool> SetIntervalAsync(long watchlistId, int? intervalMinutes, CancellationToken ct);

    /// <summary>Adds a board to a watchlist, or refreshes the company name of the existing entry.</summary>
    Task<WatchlistEntry?> AddEntryAsync(
        long watchlistId,
        string sourceId,
        string boardId,
        string companyName,
        string? configuration,
        CancellationToken ct);

    Task<bool> RemoveEntryAsync(long entryId, CancellationToken ct);

    Task<bool> SetEntryEnabledAsync(long entryId, bool enabled, CancellationToken ct);

    /// <summary>Disables every entry pointing at a board - used when the board itself is gone.</summary>
    Task<int> DisableBoardAsync(string sourceId, string boardId, CancellationToken ct);
}
