namespace JobsPulse.Core.Abstractions;

/// <summary>
/// One-shot jobs in flight across processes (`traversal_run`). The polling and the registry jobs run side by side on
/// separate runners and share one outbox, so «nothing is walking» can only be answered by the database - the
/// in-process <see cref="ITraversalProgressTracker"/> sees its own process only.
/// </summary>
public interface ITraversalRunStorage
{
    /// <summary>Registers a run and returns its id. Long-finished and long-dead rows are dropped on the way.</summary>
    Task<long> StartAsync(string role, CancellationToken ct);

    Task HeartbeatAsync(long id, CancellationToken ct);

    Task FinishAsync(long id, CancellationToken ct);

    /// <summary>
    /// Whether any run is still walking: not finished and with a heartbeat at or after <paramref name="aliveSince"/>.
    /// A runner killed without finishing its row stops counting once its heartbeat is that old.
    /// </summary>
    Task<bool> AnyActiveAsync(DateTimeOffset aliveSince, CancellationToken ct);
}
