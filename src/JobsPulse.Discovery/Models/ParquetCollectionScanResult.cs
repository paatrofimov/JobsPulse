namespace JobsPulse.Discovery.Models;

/// <summary>
/// Outcome of one crawl collection read from the columnar index. One scan answers for every ATS at once, so the
/// tokens are grouped by source; the completion rules are the same as for the http reader.
/// </summary>
public sealed record ParquetCollectionScanResult
{
    public long Records { get; init; }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> TokensBySource { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>Parquet files the collection consists of.</summary>
    public int FilesListed { get; init; }

    /// <summary>Files left after the probes - the only ones the wide columns were read from.</summary>
    public int FilesSelected { get; init; }

    public int FilesScanned { get; init; }

    /// <summary>At least one query never answered, so some file may hold boards nobody has seen.</summary>
    public bool Failed { get; init; }

    /// <summary>The run has hit <c>MaxNewTokensPerRun</c> for every source, so the rest was not read.</summary>
    public bool CapReached { get; init; }

    public bool Completed => !Failed && !CapReached;

    public int Tokens => TokensBySource.Values.Sum(t => t.Count);

    public string Status => Failed
        ? "parquet queries have failed"
        : CapReached
            ? "token cap reached"
            : "fully scanned";
}
