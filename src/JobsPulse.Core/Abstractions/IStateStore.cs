using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Core.Abstractions;

public interface IStateStore
{
    Task<IReadOnlyDictionary<string, Vacancy>> LoadSeenAsync(string sourceId, string boardId, CancellationToken ct);

    Task<IReadOnlyList<SeenVacancySnapshot>> LoadAllAsync(CancellationToken ct);

    Task<StateCommitResult> CommitAsync(StateCommit commit, CancellationToken ct);

    /// <summary>Open vacancies stored under a filter hash that is no longer in use.</summary>
    Task<IReadOnlyList<SeenVacancySnapshot>> LoadStaleFilterAsync(
        IReadOnlyList<string> knownFilterHashes, int limit, CancellationToken ct);

    Task<int> DeleteAsync(IReadOnlyList<VacancyKey> keys, CancellationToken ct);

    Task<int> SetFilterHashAsync(IReadOnlyList<VacancyKey> keys, string filterHash, CancellationToken ct);

    /// <summary>Number of open (matching) vacancies per '{sourceId}/{boardId}'.</summary>
    Task<IReadOnlyDictionary<string, int>> CountOpenByBoardAsync(CancellationToken ct);

    Task<PurgeResult> PurgeAllAsync(CancellationToken ct);
}
