namespace JobsPulse.Core.Infrastructure;

/// <summary>
/// The `traversal_run` row of the one-shot job this process runs. The commit stamps it on the watchlist history, so
/// the report of a run counts its own changes and not those of a job walking at the same time. Null outside a job.
/// </summary>
public sealed class CurrentTraversalRun
{
    // 0 is never an identity value, so it stands for «no run» and the field stays a plain atomic long.
    private long id;

    public long? Id
    {
        get => Interlocked.Read(ref id) is var value and not 0 ? value : null;
        set => Interlocked.Exchange(ref id, value ?? 0);
    }
}
