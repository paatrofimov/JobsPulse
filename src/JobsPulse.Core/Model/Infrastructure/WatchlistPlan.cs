using JobsPulse.Core.Pipeline;

namespace JobsPulse.Core.Model.Infrastructure;

/// <summary>
/// The enabled watchlists collapsed into work: one item per distinct board, and the set of filters that decides what
/// is worth storing globally. Built once per cycle and shared by the watchlist cycle, the registry cycle and the
/// filter maintenance, so all three agree on what «relevant» means.
/// </summary>
public sealed record WatchlistPlan
{
    public IReadOnlyList<BoardWorkItem> Boards { get; init; } = [];

    /// <summary>
    /// Union of the filters of every enabled watchlist. A vacancy matching none of them cannot produce a
    /// notification anywhere, so it is not stored at all - this is what keeps <c>seen_vacancy</c> bounded while the
    /// registry sweep walks thousands of boards.
    /// </summary>
    public IReadOnlyList<FilterSpec> StorageFilters { get; init; } = [];

    public string StorageFilterHash { get; init; } = string.Empty;

    public bool HasWatchlists => StorageFilters.Count > 0;

    public static readonly WatchlistPlan Empty = new();

    public static WatchlistPlan Build(IReadOnlyList<Watchlist> watchlists)
    {
        var enabled = watchlists.Where(w => w.Enabled).ToList();
        if (enabled.Count == 0)
            return Empty;

        var boards = new Dictionary<string, BoardWorkItem>(StringComparer.OrdinalIgnoreCase);

        foreach (var watchlist in enabled)
        {
            var subscription = new WatchlistSubscription
            {
                WatchlistId = watchlist.Id,
                WatchlistName = watchlist.Name,
                CompanyName = string.Empty,
                Filter = watchlist.Filter,
                FilterHash = VacancyHasher.ComputeFilterHash(watchlist.Filter)
            };

            foreach (var entry in watchlist.Entries.Where(e => e.Enabled))
            {
                var existing = boards.GetValueOrDefault(entry.BoardKey);

                var subscriptions = existing?.Subscriptions ?? [];

                boards[entry.BoardKey] = new BoardWorkItem
                {
                    SourceId = entry.VacancySourceId,
                    BoardId = entry.BoardId,
                    CompanyName = existing?.CompanyName ?? entry.CompanyName,
                    // The same board in two watchlists carries the same configuration - either entry answers.
                    Configuration = existing?.Configuration ?? entry.Configuration,
                    Subscriptions = [.. subscriptions, subscription with { CompanyName = entry.CompanyName }],
                    IntervalMinutesOverride = Min(existing?.IntervalMinutesOverride, watchlist.IntervalMinutesOverride)
                };
            }
        }

        var filters = enabled
            .Select(w => w.Filter)
            .DistinctBy(VacancyHasher.ComputeFilterHash, StringComparer.Ordinal)
            .ToList();

        return new WatchlistPlan
        {
            Boards = [.. boards.Values],
            StorageFilters = filters,
            StorageFilterHash = VacancyHasher.ComputeFilterSetHash(filters)
        };
    }

    /// <summary>The most impatient watchlist wins - a shared board is polled as often as its fastest owner asks.</summary>
    private static int? Min(int? left, int? right) => (left, right) switch
    {
        (null, null) => null,
        (null, { } r) => r,
        ({ } l, null) => l,
        var (l, r) => Math.Min(l!.Value, r!.Value)
    };
}
