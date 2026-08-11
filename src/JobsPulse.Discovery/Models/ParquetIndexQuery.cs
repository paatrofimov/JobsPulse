namespace JobsPulse.Discovery.Models;

/// <summary>One query over a batch of remote parquet files, covering every ATS at once.</summary>
public sealed record ParquetIndexQuery
{
    /// <summary>Absolute http urls - DuckDB reads them in place, over range requests.</summary>
    public required IReadOnlyList<string> Files { get; init; }

    public required IReadOnlyList<BoardIndexTarget> Targets { get; init; }

    public int FetchStatus { get; init; } = 200;

    public int PathSegments { get; init; } = 3;
}
