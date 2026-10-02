namespace JobsPulse.Core.Model.Infrastructure;

/// <summary>The report of one discovery run.</summary>
/// <param name="Report">What the run mined; null when it stopped before reporting (a deadline or a failure).</param>
public sealed record DiscoveryRunReport(
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    bool Full,
    BoardDiscoveryReport? Report);
