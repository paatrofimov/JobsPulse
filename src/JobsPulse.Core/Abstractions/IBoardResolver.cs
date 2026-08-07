using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Core.Abstractions;

public interface IBoardResolver
{
    Task<IReadOnlyList<BoardCandidate>> ResolveByNameAsync(string companyName, CancellationToken ct);

    Task<BoardCandidate?> ResolveByUrlAsync(string url, CancellationToken ct);

    Task<BoardCandidate?> ProbeAsync(string boardId, CancellationToken ct);
}