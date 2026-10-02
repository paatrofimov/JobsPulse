using JobsPulse.Core.Pipeline;

namespace JobsPulse.Core.Model.Infrastructure;

/// <summary>The report of one polling or registry run for one watchlist.</summary>
/// <param name="Cycle">What the run walked; null when it stopped before reporting (a deadline or a failure).</param>
/// <param name="Stats">The changes of this run in the watchlist - see <c>WatchlistStatsService.ComputeRunAsync</c>.</param>
public sealed record TraversalRunReport(TraversalKind Kind, CycleReport? Cycle, WatchlistStats Stats);
