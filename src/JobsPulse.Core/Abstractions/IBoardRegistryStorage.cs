using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Core.Abstractions;

public interface IBoardRegistryStorage
{
    /// <summary>Inserts unknown boards and refreshes the known ones. Returns the number of newly inserted rows.</summary>
    Task<int> UpsertAsync(IReadOnlyList<RegisteredBoard> boards, CancellationToken ct);

    Task<IReadOnlyList<RegisteredBoard>> ListAsync(string? sourceId, int limit, CancellationToken ct);

    Task<IReadOnlyDictionary<string, int>> CountBySourceAsync(CancellationToken ct);

    Task<IReadOnlyCollection<string>> GetKnownBoardIdsAsync(string sourceId, CancellationToken ct);

    Task<bool> RemoveAsync(string sourceId, string boardId, CancellationToken ct);

    Task SetActiveAsync(string sourceId, string boardId, bool isActive, CancellationToken ct);

    Task<IReadOnlyCollection<string>> GetProcessedCrawlsAsync(string sourceId, CancellationToken ct);

    Task MarkCrawlProcessedAsync(CrawlIndexProgress progress, CancellationToken ct);
}
