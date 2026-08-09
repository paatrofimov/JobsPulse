using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Core.Abstractions;

public interface IStateStore
{
    Task<IReadOnlyDictionary<string, Vacancy>> LoadSeenAsync(string sourceId, string boardId, CancellationToken ct);

    Task<IReadOnlyList<SeenVacancySnapshot>> LoadAllAsync(CancellationToken ct);

    Task<StateCommitResult> CommitAsync(StateCommit commit, CancellationToken ct);

    Task<PurgeResult> PurgeAllAsync(CancellationToken ct);
}
