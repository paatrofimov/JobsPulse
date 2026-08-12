using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Core.Abstractions;

public interface IStateStore
{
    /// <summary>Open vacancies of a board - global state, not bound to any watchlist.</summary>
    Task<IReadOnlyDictionary<string, Vacancy>> LoadSeenAsync(string sourceId, string boardId, CancellationToken ct);

    /// <summary>Match layer of a board: which watchlist reported which vacancy, and with which content.</summary>
    Task<IReadOnlyList<WatchlistMatch>> LoadMatchesAsync(string sourceId, string boardId, CancellationToken ct);

    Task<IReadOnlyList<SeenVacancySnapshot>> LoadAllAsync(CancellationToken ct);

    /// <summary>
    /// Open vacancies currently matching one watchlist, newest first, capped at <paramref name="limit"/>. The bot
    /// groups them by company and pages them itself, so it needs the whole set rather than a database page; the cap
    /// is what keeps a match-everything watchlist from loading its entire feed.
    /// <see cref="CountMatchesByWatchlistAsync"/> only gives the totals.
    /// </summary>
    Task<IReadOnlyList<Vacancy>> LoadMatchedVacanciesAsync(
        long watchlistId,
        int limit,
        CancellationToken ct);

    Task<StateCommitResult> CommitAsync(StateCommit commit, CancellationToken ct);

    /// <summary>Open vacancies stored under a filter hash that is no longer in use.</summary>
    Task<IReadOnlyList<SeenVacancySnapshot>> LoadStaleFilterAsync(
        IReadOnlyList<string> knownFilterHashes, int limit, CancellationToken ct);

    Task<int> DeleteAsync(IReadOnlyList<VacancyKey> keys, CancellationToken ct);

    Task<int> SetFilterHashAsync(IReadOnlyList<VacancyKey> keys, string filterHash, CancellationToken ct);

    /// <summary>Number of open vacancies per '{sourceId}/{boardId}'.</summary>
    Task<IReadOnlyDictionary<string, int>> CountOpenByBoardAsync(CancellationToken ct);

    /// <summary>Number of vacancies matching each watchlist, by watchlist id.</summary>
    Task<IReadOnlyDictionary<long, int>> CountMatchesByWatchlistAsync(CancellationToken ct);

    Task<PurgeResult> PurgeAllAsync(CancellationToken ct);
}
