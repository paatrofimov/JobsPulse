namespace JobsPulse.Core.Model.Infrastructure;

/// <summary>
/// One source inside a traversal: how much of it the current cycle took on, and how much of its dataset has been
/// walked at all. The same record is both the plan a cycle announces and the coverage it reports back.
/// </summary>
public sealed record TraversalSourceUnits
{
    public required string SourceId { get; init; }

    /// <summary>Units the current cycle intends to process - due boards, not the whole dataset.</summary>
    public int Planned { get; init; }

    /// <summary>Units the source has in total: watchlist boards, or active registry boards.</summary>
    public int DatasetTotal { get; init; }

    /// <summary>Units already traversed - the numerator of the dataset percentage.</summary>
    public int DatasetCovered { get; init; }
}
