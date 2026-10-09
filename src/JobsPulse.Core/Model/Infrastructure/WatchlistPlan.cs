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
    /// Union of the non-empty filters of every enabled watchlist. A vacancy matching none of them cannot produce a
    /// notification anywhere, so it is not stored at all - this is what keeps <c>seen_vacancy</c> bounded while the
    /// registry sweep walks thousands of boards. An empty filter is left out: it would make every board store
    /// everything, and it applies to the boards of its own watchlist only - see <see cref="MatchAllBoards"/>.
    /// </summary>
    public IReadOnlyList<FilterSpec> StorageFilters { get; init; } = [];

    public string StorageFilterHash { get; init; } = string.Empty;

    /// <summary>Hash of the description rules among <see cref="StorageFilters"/> - see <c>Vacancy.DescriptionRulesHash</c>.</summary>
    public string DescriptionRulesHash { get; init; } = string.Empty;

    /// <summary>Board keys of the enabled watchlists with an empty filter - everything they list is stored.</summary>
    public IReadOnlySet<string> MatchAllBoards { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Filter hash of the rows of <see cref="MatchAllBoards"/>. The board keys are part of it, so a board leaving such
    /// a watchlist makes its rows stale and the filter maintenance cleans them up.
    /// </summary>
    public string MatchAllFilterHash { get; init; } = string.Empty;

    public bool HasWatchlists { get; init; }

    public static readonly WatchlistPlan Empty = new();

    /// <summary>The storage filters and their hash for one board: everything on a board of a match-all watchlist.</summary>
    public (IReadOnlyList<FilterSpec> Filters, string Hash) StorageFor(string boardKey) =>
        MatchAllBoards.Contains(boardKey)
            ? ([FilterSpec.MatchAll], MatchAllFilterHash)
            : (StorageFilters, StorageFilterHash);

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
            .Where(w => !w.Filter.IsEmpty)
            .Select(w => w.Filter)
            .DistinctBy(VacancyHasher.ComputeFilterHash, StringComparer.Ordinal)
            .ToList();

        var matchAll = enabled
            .Where(w => w.Filter.IsEmpty)
            .SelectMany(w => w.Entries.Where(e => e.Enabled).Select(e => e.BoardKey))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new WatchlistPlan
        {
            Boards = [.. boards.Values],
            StorageFilters = filters,
            StorageFilterHash = VacancyHasher.ComputeFilterSetHash(filters),
            DescriptionRulesHash = VacancyHasher.ComputeDescriptionRulesHash(filters),
            MatchAllBoards = matchAll,
            MatchAllFilterHash = matchAll.Count == 0 ? string.Empty : VacancyHasher.ComputeMatchAllHash(matchAll),
            HasWatchlists = true
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
