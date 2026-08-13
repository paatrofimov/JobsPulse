namespace JobsPulse.Core.Model.Infrastructure;

/// <summary>One source of a <see cref="TraversalProgress"/> snapshot: its cycle counters and its dataset coverage.</summary>
public sealed record TraversalSourceProgress
{
    public required string SourceId { get; init; }

    public int Planned { get; init; }

    public int Done { get; init; }

    public int Failed { get; init; }

    public int DatasetTotal { get; init; }

    public int DatasetCovered { get; init; }

    public int CyclePercent => TraversalProgress.Percent(Done, Planned);

    public int DatasetPercent => TraversalProgress.Percent(DatasetCovered, DatasetTotal);
}
