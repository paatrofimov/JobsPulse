using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Core.Pipeline;

/// <summary>
/// Pure detection over one board fetch - no IO, no clock. Works on two levels:
/// the global one (<c>seen_vacancy</c>: what exists on the board) and the per-watchlist one
/// (<c>watchlist_vacancy</c>: what passes a filter and was reported to whom).
/// </summary>
public sealed class ChangeDetector(VacancyMatcher matcher)
{
    public sealed record Input
    {
        public required string SourceId { get; init; }
        public required string BoardId { get; init; }

        public required SourceTraverseResult Traverse { get; init; }

        /// <summary>Filters that decide what is stored globally - the union of all enabled watchlists.</summary>
        public required IReadOnlyList<FilterSpec> StorageFilters { get; init; }

        /// <summary>Watchlists interested in this board. Empty for a registry board: state only, no notifications.</summary>
        public required IReadOnlyList<WatchlistSubscription> Subscriptions { get; init; }

        /// <summary>Open rows of this board, by post id.</summary>
        public required IReadOnlyDictionary<string, Vacancy> Seen { get; init; }

        /// <summary>Existing match rows of this board, across all watchlists.</summary>
        public required IReadOnlyList<WatchlistMatch> Matches { get; init; }
    }

    public sealed record Output
    {
        public required IReadOnlyList<VacancyChange> VacanciesChanges { get; init; }
        public required IReadOnlyList<Vacancy> VacanciesUpserts { get; init; }
        public required IReadOnlyList<string> ClosedPostIds { get; init; }
        public required IReadOnlyList<WatchlistMatch> MatchUpserts { get; init; }
        public required IReadOnlyList<WatchlistMatchKey> MatchRemovals { get; init; }

        public static readonly Output Empty = new()
        {
            VacanciesChanges = [],
            VacanciesUpserts = [],
            ClosedPostIds = [],
            MatchUpserts = [],
            MatchRemovals = []
        };
    }

    public Output Detect(Input input)
    {
        // Single vacancy can be duplicated in many posts (locations/languages).
        // Deduplicate by (GroupId, Location) once - both levels see the same set.
        var fetched = Deduplicate(input.Traverse.Vacancies).ToList();
        var hashes = fetched.ToDictionary(v => v.PostId, VacancyHasher.Compute, StringComparer.Ordinal);

        var upserts = fetched
            .Where(v => input.StorageFilters.Any(f => matcher.Matches(v, f)))
            .ToList();

        // Closed can be set only if complete board was traversed.
        // Otherwise can false-positively decide that unfetched vacancy is closed.
        var closed = new List<string>();
        if (input.Traverse.IsComplete)
        {
            var present = upserts.Select(v => v.PostId).ToHashSet(StringComparer.Ordinal);
            closed.AddRange(input.Seen.Keys.Where(postId => !present.Contains(postId)));
        }

        var changes = new List<VacancyChange>();
        var matchUpserts = new List<WatchlistMatch>();
        var matchRemovals = new List<WatchlistMatchKey>();

        foreach (var subscription in input.Subscriptions)
        {
            DetectForWatchlist(input, subscription, fetched, hashes, changes, matchUpserts, matchRemovals);
        }

        return new Output
        {
            VacanciesChanges = changes,
            VacanciesUpserts = upserts,
            ClosedPostIds = closed,
            MatchUpserts = matchUpserts,
            MatchRemovals = matchRemovals
        };
    }

    private void DetectForWatchlist(
        Input input,
        WatchlistSubscription subscription,
        IReadOnlyList<Vacancy> fetched,
        IReadOnlyDictionary<string, string> hashes,
        List<VacancyChange> changes,
        List<WatchlistMatch> matchUpserts,
        List<WatchlistMatchKey> matchRemovals)
    {
        var previous = input.Matches
            .Where(m => m.WatchlistId == subscription.WatchlistId)
            .ToDictionary(m => m.PostId, m => m.ContentHash, StringComparer.Ordinal);

        var matchedNow = new HashSet<string>(StringComparer.Ordinal);

        foreach (var vacancy in fetched.Where(v => matcher.Matches(v, subscription.Filter)))
        {
            var hash = hashes[vacancy.PostId];
            matchedNow.Add(vacancy.PostId);

            matchUpserts.Add(new WatchlistMatch
            {
                WatchlistId = subscription.WatchlistId,
                SourceId = input.SourceId,
                BoardId = input.BoardId,
                PostId = vacancy.PostId,
                ContentHash = hash,
                FilterHash = subscription.FilterHash
            });

            if (!previous.TryGetValue(vacancy.PostId, out var reported))
                changes.Add(Change(VacancyChangeKind.New, vacancy, hash, subscription));
            else if (!string.Equals(reported, hash, StringComparison.Ordinal))
                changes.Add(Change(VacancyChangeKind.Updated, vacancy, hash, subscription));
        }

        // Nothing can be closed for a watchlist while the board itself is only partially known.
        if (!input.Traverse.IsComplete)
            return;

        foreach (var (postId, reported) in previous.Where(p => !matchedNow.Contains(p.Key)))
        {
            matchRemovals.Add(new WatchlistMatchKey(
                subscription.WatchlistId, input.SourceId, input.BoardId, postId));

            // The post is gone from the board (or from the filter) - the notification is rebuilt from stored state.
            if (input.Seen.TryGetValue(postId, out var stored))
                changes.Add(Change(VacancyChangeKind.Closed, stored, reported, subscription));
        }
    }

    private static IEnumerable<Vacancy> Deduplicate(IReadOnlyList<Vacancy> vacancies)
    {
        var seenGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenPosts = new HashSet<string>(StringComparer.Ordinal);

        foreach (var v in vacancies)
        {
            // A board answering the same post twice would break the per-post hash lookup.
            if (!seenPosts.Add(v.PostId))
                continue;

            // prospect-post, skipping
            if (string.IsNullOrEmpty(v.GroupId))
            {
                yield return v;
                continue;
            }

            if (seenGroups.Add($"{v.GroupId}|{v.Location}"))
                yield return v;
        }
    }

    private static VacancyChange Change(
        VacancyChangeKind kind,
        Vacancy vacancy,
        string hash,
        WatchlistSubscription subscription) =>
        new()
        {
            Kind = kind,
            Vacancy = vacancy,
            ContentHash = hash,
            WatchlistId = subscription.WatchlistId,
            WatchlistName = subscription.WatchlistName,
            CompanyName = subscription.CompanyName
        };
}
