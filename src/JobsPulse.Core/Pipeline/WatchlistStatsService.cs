using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Core.Pipeline;

/// <summary>Statistics of one watchlist - the IO around <see cref="WatchlistStatsCalculator"/>.</summary>
public sealed class WatchlistStatsService(
    IWatchlistEventStorage events,
    IStateStore stateStore,
    TimeProvider clock)
{
    /// <summary>The last <paramref name="days"/> days, ending now.</summary>
    public async Task<WatchlistStats> ComputeAsync(Watchlist watchlist, int days, CancellationToken ct)
    {
        var to = clock.GetUtcNow();

        return await ComputeAsync(watchlist, to.AddDays(-days), to, days, counts: null, ct);
    }

    /// <summary>An arbitrary period - a digest since the previous one. <c>Days</c> is the period rounded up.</summary>
    public async Task<WatchlistStats> ComputeAsync(
        Watchlist watchlist,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct)
    {
        var days = Math.Max(1, (int)Math.Ceiling((to - from).TotalDays));

        return await ComputeAsync(watchlist, from, to, days, counts: null, ct);
    }

    /// <summary>
    /// What one run changed between its start and its end. With a run id only the events that run committed are
    /// counted, so a job walking at the same time does not leak into the report; without one, the whole window is.
    /// </summary>
    public async Task<WatchlistStats> ComputeRunAsync(
        Watchlist watchlist,
        long? runId,
        DateTimeOffset startedAt,
        DateTimeOffset finishedAt,
        CancellationToken ct)
    {
        Func<WatchlistEvent, bool>? counts = runId is { } id
            ? e => e.RunId == id
            : null;

        return await ComputeAsync(watchlist, startedAt, finishedAt, days: 0, counts, ct);
    }

    private async Task<WatchlistStats> ComputeAsync(
        Watchlist watchlist,
        DateTimeOffset from,
        DateTimeOffset to,
        int days,
        Func<WatchlistEvent, bool>? counts,
        CancellationToken ct)
    {
        // The whole history is read, not only the period: whether a company had anything open when the period
        // began depends on what happened before it.
        var history = await events.LoadAsync(watchlist.Id, to, ct);
        var activity = await stateStore.CountBoardActivityAsync(from, ct);

        return WatchlistStatsCalculator.Compute(watchlist, history, activity, from, to, days, counts);
    }
}
