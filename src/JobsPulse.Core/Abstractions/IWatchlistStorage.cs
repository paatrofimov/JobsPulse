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

    /// <summary>Returns null when the name is already taken. A null owner creates a system watchlist.</summary>
    Task<Watchlist?> CreateAsync(string name, FilterSpec filter, long? ownerUserId, CancellationToken ct);

    /// <summary>
    /// Gives every ownerless (system) watchlist to one user and returns how many were taken over. The legacy import
    /// and the admin commands create watchlists with no owner, and nobody but an admin can edit those - claiming them
    /// is what turns them into ordinary, fully editable lists.
    /// </summary>
    Task<int> ClaimOwnerlessAsync(long ownerUserId, CancellationToken ct);

    /// <summary>Returns false when the watchlist is gone or the new name is already taken.</summary>
    Task<bool> RenameAsync(long watchlistId, string name, CancellationToken ct);

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

    /// <summary>
    /// Adds a board discovery has promoted. Insert-only: an existing entry - enabled or disabled - is left
    /// untouched and null is returned, so a board the user has dropped is never resurrected by the next sweep.
    /// </summary>
    Task<WatchlistEntry?> AddDiscoveredEntryAsync(
        long watchlistId,
        string sourceId,
        string boardId,
        string companyName,
        string? configuration,
        CancellationToken ct);

    Task<bool> RemoveEntryAsync(long entryId, CancellationToken ct);

    Task<bool> SetEntryEnabledAsync(long entryId, bool enabled, CancellationToken ct);

    /// <summary>Marks a company as worked through (a CV went out) or clears the mark.</summary>
    Task<bool> SetEntryWorkedAsync(long entryId, bool worked, CancellationToken ct);

    /// <summary>Disables every entry pointing at a board - used when the board itself is gone.</summary>
    Task<int> DisableBoardAsync(string sourceId, string boardId, CancellationToken ct);
}
