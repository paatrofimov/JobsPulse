namespace JobsPulse.Core.Model.Infrastructure;

public readonly record struct WatchlistMatchKey(long WatchlistId, string SourceId, string BoardId, string PostId)
{
    public override string ToString() => $"{WatchlistId}:{SourceId}/{BoardId}/{PostId}";
}
