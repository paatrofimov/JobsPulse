namespace JobsPulse.Core.Model.Infrastructure;

public readonly record struct BoardDiscoveryReport(
    bool Started,
    int CollectionsProcessed,
    long RecordsSeen,
    int TokensFound,
    int Validated,
    int BoardsAdded)
{
    public static readonly BoardDiscoveryReport Busy = new(false, 0, 0, 0, 0, 0);
}
