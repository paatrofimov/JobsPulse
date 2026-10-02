namespace JobsPulse.Core.Model.Infrastructure;

public readonly record struct BoardDiscoveryReport(
    bool Started,
    int CollectionsProcessed,
    long RecordsSeen,
    int TokensFound,
    int Validated,
    int BoardsAdded,
    int CollectionsFailed = 0,
    int CollectionsPending = 0)
{
    /// <summary>The first crawl index of the window the run walked, e.g. 'CC-MAIN-2026-31' - what period it mined.</summary>
    public string? FirstCollection { get; init; }

    /// <summary>The last crawl index of the window the run walked.</summary>
    public string? LastCollection { get; init; }

    public static readonly BoardDiscoveryReport Busy = new(false, 0, 0, 0, 0, 0);

    public static readonly BoardDiscoveryReport Empty = new(true, 0, 0, 0, 0, 0);
}
