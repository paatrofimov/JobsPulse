namespace JobsPulse.Core.Model.Infrastructure;

/// <summary>
/// How much of the crawl dataset discovery has mined: the published crawl indexes against the ones already recorded
/// in `crawl_index_state`, per source. `CollectionsTotal` is 0 when the index could not be asked - the number is
/// nice to have, not worth failing a screen over.
/// </summary>
public sealed record DiscoveryProgress
{
    public static readonly DiscoveryProgress None = new();

    public bool IsRunning { get; init; }

    public int CollectionsTotal { get; init; }

    public IReadOnlyDictionary<string, int> ProcessedBySource { get; init; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
}
