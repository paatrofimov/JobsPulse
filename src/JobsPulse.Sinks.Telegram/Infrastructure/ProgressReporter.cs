using JobsPulse.Core.Abstractions;

namespace JobsPulse.Sinks.Telegram.Infrastructure;

/// <summary>
/// Gathers the traversal progress from its two sources of truth - the in-memory cycle tracker and
/// the board registry - and hands it to <see cref="ProgressFormatter"/>. One place, so the admin screen and the
/// <c>/progress</c> command can never drift apart or read different numbers.
/// </summary>
public sealed class ProgressReporter(
    ITraversalProgressTracker progress,
    IBoardDiscoveryService discovery,
    IBoardRegistryStorage boardRegistry,
    TimeProvider clock)
{
    public async Task<string> RenderAsync(CancellationToken ct) =>
        ProgressFormatter.Render(
            progress.Snapshot(),
            await discovery.GetProgressAsync(ct),
            await boardRegistry.CountBySourceAsync(ct),
            clock.GetUtcNow());
}
