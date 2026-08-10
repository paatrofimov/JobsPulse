namespace JobsPulse.Discovery.Models;

/// <summary>
/// Outcome of one crawl collection scan. Only a <see cref="Completed"/> collection may be marked processed -
/// anything else stays pending and is walked again by the next run.
/// </summary>
public sealed record CollectionScanResult
{
    public long Records { get; init; }

    public IReadOnlyList<string> Tokens { get; init; } = [];

    /// <summary>At least one index request inside the collection never answered.</summary>
    public bool Failed { get; init; }

    /// <summary>The run has hit <c>MaxNewTokensPerRun</c>, so the rest of the collection was not read.</summary>
    public bool CapReached { get; init; }

    public bool Completed => !Failed && !CapReached;

    public string Status => Failed
        ? "index requests have failed"
        : CapReached
            ? "token cap reached"
            : "fully scanned";
}
