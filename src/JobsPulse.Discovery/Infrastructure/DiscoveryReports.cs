using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Discovery.Infrastructure;

/// <summary>Adding up the per-collection outcomes of a pass - shared by every discovery pass.</summary>
public static class DiscoveryReports
{
    public static BoardDiscoveryReport Pending(int collections) =>
        new(true, 0, 0, 0, 0, 0, 0, Math.Max(0, collections));

    public static BoardDiscoveryReport Merge(BoardDiscoveryReport a, BoardDiscoveryReport b) => new(
        true,
        a.CollectionsProcessed + b.CollectionsProcessed,
        a.RecordsSeen + b.RecordsSeen,
        a.TokensFound + b.TokensFound,
        a.Validated + b.Validated,
        a.BoardsAdded + b.BoardsAdded,
        a.CollectionsFailed + b.CollectionsFailed,
        a.CollectionsPending + b.CollectionsPending);
}
