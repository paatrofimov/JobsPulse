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
    public static readonly BoardDiscoveryReport Busy = new(false, 0, 0, 0, 0, 0);

    public static readonly BoardDiscoveryReport Empty = new(true, 0, 0, 0, 0, 0);
}
