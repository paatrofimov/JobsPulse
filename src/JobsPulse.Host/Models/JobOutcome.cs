using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Core.Pipeline;

namespace JobsPulse.Host.Models;

/// <summary>
/// What a one-shot job learned about its own run, filled while it runs - a deadline or a failure leaves the rest
/// unset, and the run report says so.
/// </summary>
public sealed class JobOutcome
{
    /// <summary>The routine did not run at all (switched off, or another cycle held the gate) - nothing to report.</summary>
    public bool Skipped { get; set; }

    public CycleReport? Cycle { get; set; }

    public bool DiscoveryFull { get; set; }

    public BoardDiscoveryReport? Discovery { get; set; }
}
