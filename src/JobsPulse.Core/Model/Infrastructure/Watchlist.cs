namespace JobsPulse.Core.Model.Infrastructure;

/// <summary>
/// A named set of boards with one filter. The watchlist is the processing boundary: the same vacancy can match in
/// one watchlist, miss in another, and produce a separate notification per watchlist.
/// </summary>
public sealed record Watchlist
{
    public long Id { get; init; }

    public required string Name { get; init; }

    /// <summary>
    /// Telegram user id of the owner - the only one allowed to change this watchlist. Null for a watchlist that
    /// predates ownership (the legacy import): it belongs to nobody, is shown as a system one and is read-only for
    /// everybody except an admin.
    /// </summary>
    public long? OwnerUserId { get; init; }

    public bool Enabled { get; init; } = true;

    /// <summary>Applied to every entry of this watchlist - there is no per-entry filter.</summary>
    public FilterSpec Filter { get; init; } = new();

    /// <summary>Overrides the global polling interval for the boards of this watchlist.</summary>
    public int? IntervalMinutesOverride { get; init; }

    public IReadOnlyList<WatchlistEntry> Entries { get; init; } = [];

    public WatchlistEntry? FindEntry(string entryIdOrCompany) =>
        long.TryParse(entryIdOrCompany, out var id)
            ? Entries.FirstOrDefault(e => e.Id == id)
            : Entries.FirstOrDefault(e =>
                string.Equals(e.CompanyName, entryIdOrCompany, StringComparison.OrdinalIgnoreCase));

    public WatchlistEntry? FindEntry(string sourceId, string boardId) =>
        Entries.FirstOrDefault(e =>
            string.Equals(e.VacancySourceId, sourceId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.BoardId, boardId, StringComparison.OrdinalIgnoreCase));
}
