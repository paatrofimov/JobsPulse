using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Core.Pipeline;

namespace JobsPulse.Host.Models;

/// <summary>
/// What the run report of a one-shot job is built from, filled while the routine runs - a deadline or a failure leaves
/// the rest unset, and the report says so.
/// </summary>
public sealed class RunReportInputs
{
    /// <summary>The routine did not run at all (switched off, or another cycle held the gate) - nothing to report.</summary>
    public bool Skipped { get; set; }

    public CycleReport? Cycle { get; set; }

    public bool DiscoveryFull { get; set; }

    public BoardDiscoveryReport? Discovery { get; set; }
}
