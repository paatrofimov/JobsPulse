using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Domain.Extensions;
using JobsPulse.Core.Model.Infrastructure;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Core.Pipeline;

/// <summary>
/// Restores the closures the watchlist history is missing from what `seen_vacancy` remembers - the history began
/// with the open matches only, and jobs on older code did not write it at all. Run by `--role historyrepair`;
/// running it again adds nothing.
///
/// A closed row of a watchlist board is taken as a closed match when it passes the watchlist filter without its
/// description and freshness rules: descriptions are not stored, and a vacancy is judged by its date when it is
/// matched, not when it closes. An open vacancy is restored only from the match layer, which knows exactly what the
/// watchlist matches: a current match without any history gets its opening.
/// </summary>
public sealed class WatchlistHistoryRepair(
    IWatchlistStorage watchlists,
    IStateStore stateStore,
    IWatchlistEventStorage events,
    VacancyMatcher matcher,
    TimeProvider clock,
    ILog log)
{
    private readonly ILog ctxLog = log.ForContext<WatchlistHistoryRepair>();

    /// <summary>Returns how many events were restored.</summary>
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var closed = (await stateStore.LoadAllAsync(ct))
            .Where(x => x.ClosedAt is not null)
            .ToList();

        var restored = 0;

        foreach (var watchlist in await watchlists.GetAllAsync(ct))
        {
            var history = await events.LoadAsync(watchlist.Id, clock.GetUtcNow(), ct);
            var filter = watchlist.Filter with
            {
                DescriptionAnyOf = [],
                DescriptionNoneOf = [],
                PostedWithinDays = null
            };

            var matched = await stateStore.LoadMatchedVacanciesAsync(watchlist.Id, int.MaxValue, ct);

            var missing = Plan(watchlist, closed, matched, history, v => matcher.Matches(v, filter));

            restored += await events.AppendAsync(missing, ct);

            ctxLog.Info("Restored {Count} history events of watchlist {Watchlist}", missing.Count, watchlist.Id);
        }

        return restored;
    }

    /// <summary>
    /// The events a closed row adds: none when the history already ends with a removal of the post, a closure when it
    /// ends with an opening before the row closed, an opening and a closure when the post has no history at all.
    /// A current match without history adds its opening.
    /// </summary>
    public static IReadOnlyList<WatchlistEvent> Plan(
        Watchlist watchlist,
        IReadOnlyList<SeenVacancySnapshot> closedRows,
        IReadOnlyList<Vacancy> openMatches,
        IReadOnlyList<WatchlistEvent> history,
        Func<Vacancy, bool> matches)
    {
        var entries = watchlist.Entries
            .GroupBy(e => e.BoardKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var last = history
            .OrderBy(e => e.OccurredAt)
            .GroupBy(e => (Board: e.BoardKey.ToLowerInvariant(), e.PostId))
            .ToDictionary(g => g.Key, g => g.Last());

        var missing = new List<WatchlistEvent>();

        foreach (var row in closedRows)
        {
            var vacancy = row.Vacancy;
            var board = $"{vacancy.SourceId}/{vacancy.BoardId}";

            if (row.ClosedAt is not { } closedAt
                || !entries.TryGetValue(board, out var entry)
                || !matches(vacancy))
                continue;

            var known = last.GetValueOrDefault((board.ToLowerInvariant(), vacancy.PostId));

            if (known is null)
            {
                if (vacancy.FirstSeenAt is not { } firstSeenAt)
                    continue;

                missing.Add(Event(watchlist, entry, vacancy, VacancyChangeKind.New, firstSeenAt));
                missing.Add(Event(watchlist, entry, vacancy, VacancyChangeKind.Closed, closedAt));
                continue;
            }

            if (known.Kind == VacancyChangeKind.New && closedAt > known.OccurredAt)
                missing.Add(Event(watchlist, entry, vacancy, VacancyChangeKind.Closed, closedAt));
        }

        foreach (var vacancy in openMatches)
        {
            var board = $"{vacancy.SourceId}/{vacancy.BoardId}";

            if (vacancy.FirstSeenAt is not { } firstSeenAt
                || !entries.TryGetValue(board, out var entry)
                || last.ContainsKey((board.ToLowerInvariant(), vacancy.PostId)))
                continue;

            missing.Add(Event(watchlist, entry, vacancy, VacancyChangeKind.New, firstSeenAt));
        }

        return missing;
    }

    private static WatchlistEvent Event(
        Watchlist watchlist,
        WatchlistEntry entry,
        Vacancy vacancy,
        VacancyChangeKind kind,
        DateTimeOffset at) =>
        new()
        {
            WatchlistId = watchlist.Id,
            SourceId = vacancy.SourceId,
            BoardId = vacancy.BoardId,
            PostId = vacancy.PostId,
            CompanyName = entry.CompanyName,
            Kind = kind,
            Location = vacancy.LocationOrOffice(),
            OccurredAt = at
        };
}
