namespace JobsPulse.Discovery.Models;

/// <summary>Outcome of one index page. Records are counted even when the page was cut off halfway.</summary>
public sealed record PageScanResult
{
    public long Records { get; init; }

    public bool Failed { get; init; }

    public bool CapReached { get; init; }
}
