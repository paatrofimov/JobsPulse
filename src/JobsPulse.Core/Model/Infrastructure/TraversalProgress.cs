namespace JobsPulse.Core.Model.Infrastructure;

/// <summary>
/// How far one traversal has got: the state of its current (or last) cycle and the coverage of the dataset it walks,
/// both in total and per source. A snapshot, not live state - it is read by the admin screen while the cycle keeps
/// running.
/// </summary>
public sealed record TraversalProgress
{
    public required TraversalKind Kind { get; init; }

    public bool IsRunning { get; init; }

    /// <summary>Null while nothing has ever run - the process has just started.</summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>Null while the cycle is still running.</summary>
    public DateTimeOffset? FinishedAt { get; init; }

    public IReadOnlyList<TraversalSourceProgress> Sources { get; init; } = [];

    public int Planned => Sources.Sum(s => s.Planned);

    public int Done => Sources.Sum(s => s.Done);

    public int Failed => Sources.Sum(s => s.Failed);

    public int DatasetTotal => Sources.Sum(s => s.DatasetTotal);

    public int DatasetCovered => Sources.Sum(s => s.DatasetCovered);

    /// <summary>Percent of the current cycle. A cycle with nothing to do reads as complete, not as zero.</summary>
    public int CyclePercent => Percent(Done, Planned);

    /// <summary>Percent of the dataset walked - what «processed» means for the whole board set.</summary>
    public int DatasetPercent => Percent(DatasetCovered, DatasetTotal);

    public bool HasRun => StartedAt is not null;

    public static int Percent(int part, int total) =>
        total <= 0 ? 100 : (int)Math.Round(100d * Math.Min(part, total) / total);
}
