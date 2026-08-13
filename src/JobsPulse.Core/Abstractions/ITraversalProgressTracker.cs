using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Core.Abstractions;

/// <summary>
/// Live progress of the two polling cycles. The cycles already count everything they do, but only into the log, so
/// «how far has the walk got» is not answerable from the outside - this is where the counters are kept instead.
/// In-memory and process-wide: coverage is measured from the start of the process, exactly like the scheduling state
/// of <see cref="Pipeline.PollingOrchestrator"/> it mirrors.
/// </summary>
public interface ITraversalProgressTracker
{
    /// <summary>Announces the units of one cycle and resets its counters.</summary>
    void CycleStarted(TraversalKind kind, IReadOnlyList<TraversalSourceUnits> plan);

    /// <summary>One unit is finished - a single board traversal, successful or not.</summary>
    void UnitFinished(TraversalKind kind, string sourceId, bool failed);

    /// <summary>Closes the cycle and refreshes the dataset coverage it reached.</summary>
    void CycleFinished(TraversalKind kind, IReadOnlyList<TraversalSourceUnits> coverage);

    IReadOnlyList<TraversalProgress> Snapshot();
}
