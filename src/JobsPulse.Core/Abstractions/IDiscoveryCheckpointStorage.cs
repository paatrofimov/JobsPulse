using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Core.Abstractions;

/// <summary>
/// The discovery offset store (`discovery_checkpoint`), one row per iteration. Kept apart from
/// `crawl_index_state`: that table says which indexes are mined, this one says where the current walk is and how
/// much it has done.
/// </summary>
public interface IDiscoveryCheckpointStorage
{
    /// <summary>Newest iterations first. Two are all the bot needs - the current one and the previous one.</summary>
    Task<IReadOnlyList<DiscoveryCheckpoint>> GetLatestAsync(int count, CancellationToken ct);

    /// <summary>Upsert keyed by iteration - the same row is rewritten every few minutes while the run walks.</summary>
    Task SaveAsync(DiscoveryCheckpoint checkpoint, CancellationToken ct);
}
