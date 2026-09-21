namespace JobsPulse.Core.Model.Infrastructure;

/// <summary>
/// The offset of one discovery iteration: which crawl index the iteration started from, which one a restart
/// continues from, and what the iteration has done so far. Rewritten every few minutes while a run walks the
/// window, so the counters accumulate across restarts instead of starting over.
/// </summary>
public sealed record DiscoveryCheckpoint
{
    /// <summary>Ordinal of the iteration - how many discovery runs the installation has ever started.</summary>
    public int Iteration { get; init; }

    /// <summary>The iteration is a bootstrap (the whole window) rather than an incremental run.</summary>
    public bool Full { get; init; }

    public DateTimeOffset StartedAt { get; init; }

    /// <summary>When the offset was last written - at most <c>CheckpointIntervalMinutes</c> ago while running.</summary>
    public DateTimeOffset UpdatedAt { get; init; }

    public DateTimeOffset? FinishedAt { get; init; }

    public bool IsFinished => FinishedAt.HasValue;

    /// <summary>The first collection of the window the iteration opened with.</summary>
    public string? StartedFromCollectionId { get; init; }

    /// <summary>
    /// The first collection the iteration has not finished - where a restart picks the walk up. Null when the
    /// whole window is behind it.
    /// </summary>
    public string? ResumeFromCollectionId { get; init; }

    public int CollectionsTotal { get; init; }

    /// <summary>Collections the offset has moved past, whatever their outcome.</summary>
    public int CollectionsDone { get; init; }

    public int CollectionsProcessed { get; init; }

    public int CollectionsFailed { get; init; }

    public long RecordsSeen { get; init; }

    public int TokensFound { get; init; }

    public int BoardsAdded { get; init; }
}
