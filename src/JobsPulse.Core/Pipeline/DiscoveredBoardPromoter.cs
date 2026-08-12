using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Domain.Extensions;
using JobsPulse.Core.Model.Infrastructure;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Core.Pipeline;

/// <summary>
/// Turns a board of the discovery registry into a watchlist entry: a board nobody watches, whose vacancies pass the
/// filter of some watchlist, is added to that watchlist and reported right away.
///
/// The match rows and the notifications are written here, in the same transaction as the promotion, on purpose -
/// otherwise the next watchlist cycle would report the very same vacancies as a plain <c>New</c> wave and the
/// reader would never learn that a new board appeared.
/// </summary>
public sealed class DiscoveredBoardPromoter(
    IWatchlistStorage watchlists,
    IStateStore stateStore,
    VacancyMatcher matcher,
    IPollingTrigger pollingTrigger,
    ILog log)
{
    private readonly ILog ctxLog = log.ForContext<DiscoveredBoardPromoter>();

    /// <summary>
    /// Candidates are the watchlists that may absorb a discovered board: enabled and with a non-empty filter.
    /// A watchlist matching everything would absorb the whole registry, which is never what the reader asked for.
    /// </summary>
    public static IReadOnlyList<Watchlist> SelectCandidates(IReadOnlyList<Watchlist> watchlists) =>
        [.. watchlists.Where(w => w.Enabled && !w.Filter.IsEmpty)];

    /// <summary>
    /// Promotes one registry board into every candidate watchlist its vacancies match. Returns what was promoted -
    /// empty when nothing matched or every candidate already knows the board.
    /// </summary>
    public async Task<IReadOnlyList<BoardPromotion>> TryPromoteAsync(
        RegisteredBoard board,
        IReadOnlyList<Vacancy> relevant,
        IReadOnlyList<Watchlist> candidates,
        CancellationToken ct)
    {
        if (relevant.Count == 0 || candidates.Count == 0)
            return [];

        var companyName = board.DisplayName ?? board.BoardId;
        var promotions = new List<BoardPromotion>();

        foreach (var watchlist in candidates)
        {
            var matched = matcher.Apply(relevant, watchlist.Filter);
            if (matched.Count == 0)
                continue;

            var entry = await watchlists.AddDiscoveredEntryAsync(
                watchlist.Id, board.SourceId, board.BoardId, companyName, board.Configuration, ct);

            // The board is already listed there - by hand, or dropped by hand, which must stay dropped.
            if (entry is null)
                continue;

            var reported = await ReportAsync(board, watchlist, entry.CompanyName, matched, ct);

            ctxLog.Info(
                "Discovered board {Board} ({Company}) promoted to watchlist {Watchlist}: "
                + "{Matched} matching vacancies, {Reported} reported",
                entry.BoardKey, entry.CompanyName, watchlist.Name, matched.Count, reported);

            promotions.Add(new BoardPromotion(
                entry.BoardKey, entry.CompanyName, watchlist.Id, watchlist.Name, matched.Count));
        }

        // A brand new entry has no run stamp, so it is due at once - do not wait for the next scheduled cycle.
        if (promotions.Count > 0)
            pollingTrigger.RequestImmediateRun();

        return promotions;
    }

    /// <summary>Match rows and notifications in one commit: the report can never exist without the state.</summary>
    private async Task<int> ReportAsync(
        RegisteredBoard board,
        Watchlist watchlist,
        string companyName,
        IReadOnlyList<Vacancy> matched,
        CancellationToken ct)
    {
        var filterHash = VacancyHasher.ComputeFilterHash(watchlist.Filter);

        var matchUpserts = new List<WatchlistMatch>(matched.Count);
        var notifications = new List<OutboxItem>(matched.Count);

        foreach (var vacancy in matched)
        {
            var hash = VacancyHasher.Compute(vacancy);

            matchUpserts.Add(new WatchlistMatch
            {
                WatchlistId = watchlist.Id,
                SourceId = board.SourceId,
                BoardId = board.BoardId,
                PostId = vacancy.PostId,
                ContentHash = hash,
                FilterHash = filterHash
            });

            notifications.Add(new OutboxItem
            {
                DedupKey = vacancy.ToDedupKey(VacancyChangeKind.New, hash, watchlist.Id),
                ChangeKind = VacancyChangeKind.New,
                CompanyName = companyName,
                WatchlistId = watchlist.Id,
                WatchlistName = watchlist.Name,
                Discovered = true,
                Vacancy = vacancy
            });
        }

        // The vacancies themselves are already in seen_vacancy - the registry sweep has just committed them.
        var result = await stateStore.CommitAsync(new StateCommit
        {
            SourceId = board.SourceId,
            BoardId = board.BoardId,
            Upserts = [],
            ClosedPostIds = [],
            Notifications = notifications,
            MatchUpserts = matchUpserts
        }, ct);

        return result.OutboxAffectedRows;
    }
}
