namespace JobsPulse.Core.Model.Infrastructure;

/// <summary>
/// How much of the crawl dataset discovery has mined: the published crawl indexes against the ones already recorded
/// in `crawl_index_state`, per source
/// </summary>
public sealed record DiscoveryProgress
{
    public static readonly DiscoveryProgress None = new();

    public bool IsRunning { get; init; }

    public IReadOnlyDictionary<string, int> ProcessedBySource { get; init; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The iteration being walked right now, or the last one that ran. Null before the first run.</summary>
    public DiscoveryCheckpoint? Current { get; init; }

    /// <summary>The iteration before it - what «last time» did, so the current numbers have something to mean.</summary>
    public DiscoveryCheckpoint? Previous { get; init; }
}
