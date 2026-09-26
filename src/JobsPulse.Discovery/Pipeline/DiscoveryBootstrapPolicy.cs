using JobsPulse.Core.Abstractions;

namespace JobsPulse.Discovery.Pipeline;

public sealed class DiscoveryBootstrapPolicy(
    IBoardRegistryStorage registry,
    IDiscoveryCheckpointStorage checkpoints)
{
    /// <summary>
    /// A bootstrap is due while the registry is empty or the last iteration is a bootstrap cut short. The second
    /// case is what lets a process that does not live through the whole bootstrap - a restart, a time-boxed job -
    /// resume it instead of falling back to an incremental walk of the entire crawl history.
    /// </summary>
    public async Task<bool> IsBootstrapDueAsync(CancellationToken ct)
    {
        var counts = await registry.CountBySourceAsync(ct);

        if (counts.Values.Sum() == 0)
            return true;

        var latest = (await checkpoints.GetLatestAsync(1, ct)).FirstOrDefault();

        return latest is { IsFinished: false, Full: true };
    }
}
