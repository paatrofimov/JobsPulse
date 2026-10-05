using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Core.Options;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Core.Pipeline;

/// <summary>
/// The report a one-shot job sends when it is done: polling and registry report what they changed in every enabled
/// watchlist, discovery reports what it mined. Switched off by <c>Digest:RunReports</c>, and per owner by
/// <see cref="BotUser.SilentMode"/>.
/// </summary>
public sealed class RunReportService(
    IWatchlistStorage watchlists,
    IBotUserStorage users,
    WatchlistStatsService stats,
    IReportSink sink,
    IOptionsMonitor<DigestOptions> digestOptions,
    IOptionsMonitor<DeliveryOptions> deliveryOptions,
    TimeProvider clock,
    ILog log)
{
    private readonly ILog ctxLog = log.ForContext<RunReportService>();

    /// <summary>Returns how many reports were delivered.</summary>
    public async Task<int> SendTraversalAsync(
        TraversalKind kind,
        long? runId,
        DateTimeOffset startedAt,
        CycleReport? cycle,
        CancellationToken ct)
    {
        if (!digestOptions.CurrentValue.RunReports)
            return 0;

        var finishedAt = clock.GetUtcNow();

        var withCompanies = (await watchlists.GetEnabledAsync(ct))
            .Where(w => w.Entries.Count > 0)
            .ToList();

        var owners = await users.GetManyAsync(
            withCompanies.Select(w => w.OwnerUserId).OfType<long>().Distinct().ToList(),
            ct);

        var targets = withCompanies
            .Where(w => w.OwnerUserId is not { } ownerId
                        || !owners.TryGetValue(ownerId, out var owner)
                        || !owner.SilentMode)
            .ToList();

        ctxLog.Info("{Kind} run report goes to {Targets} of {Watchlists} watchlists, the rest belong to silent owners",
            kind, targets.Count, withCompanies.Count);

        var pause = TimeSpan.FromSeconds(deliveryOptions.CurrentValue.DelayBetweenMessagesSeconds);
        var delivered = 0;

        for (var i = 0; i < targets.Count; i++)
        {
            if (i > 0 && pause > TimeSpan.Zero)
                await Task.Delay(pause, clock, ct);

            var watchlist = targets[i];
            var changes = await stats.ComputeRunAsync(watchlist, runId, startedAt, finishedAt, ct);
            var result = await sink.DeliverRunAsync(watchlist, new TraversalRunReport(kind, cycle, changes), ct);

            if (result.Success)
                delivered++;
            else
                ctxLog.Warn("{Kind} run report of watchlist {Watchlist} was not delivered: {Error}",
                    kind, watchlist.Id, result.Error);
        }

        return delivered;
    }

    public async Task<bool> SendDiscoveryAsync(
        DateTimeOffset startedAt,
        bool full,
        BoardDiscoveryReport? report,
        CancellationToken ct)
    {
        if (!digestOptions.CurrentValue.RunReports)
            return false;

        var result = await sink.DeliverDiscoveryAsync(
            new DiscoveryRunReport(startedAt, clock.GetUtcNow(), full, report), ct);

        if (!result.Success)
            ctxLog.Warn("Discovery run report was not delivered: {Error}", result.Error);

        return result.Success;
    }
}
