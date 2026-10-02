using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Options;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Core.Pipeline;

/// <summary>
/// Sends every enabled watchlist with at least one company its statistics for the last <c>Digest:PeriodDays</c>.
/// Run by `--role digest`; how often is decided by whoever starts the job (cron-job.org), not here.
/// </summary>
public sealed class DigestService(
    IWatchlistStorage watchlists,
    WatchlistStatsService stats,
    IReportSink sink,
    IOptionsMonitor<DigestOptions> digestOptions,
    IOptionsMonitor<DeliveryOptions> deliveryOptions,
    TimeProvider clock,
    ILog log)
{
    private readonly ILog ctxLog = log.ForContext<DigestService>();

    /// <summary>Returns how many digests were delivered.</summary>
    public async Task<int> SendAsync(CancellationToken ct)
    {
        var days = digestOptions.CurrentValue.PeriodDays;

        var targets = (await watchlists.GetEnabledAsync(ct))
            .Where(w => w.Entries.Count > 0)
            .ToList();

        var pause = TimeSpan.FromSeconds(deliveryOptions.CurrentValue.DelayBetweenMessagesSeconds);
        var delivered = 0;

        for (var i = 0; i < targets.Count; i++)
        {
            if (i > 0 && pause > TimeSpan.Zero)
                await Task.Delay(pause, clock, ct);

            var watchlist = targets[i];
            var digest = await stats.ComputeAsync(watchlist, days, ct);
            var result = await sink.DeliverDigestAsync(watchlist, digest, ct);

            if (result.Success)
                delivered++;
            else
                ctxLog.Warn("Digest of watchlist {Watchlist} was not delivered: {Error}", watchlist.Id, result.Error);
        }

        ctxLog.Info("Delivered {Delivered} of {Total} digests for {Days} days", delivered, targets.Count, days);

        return delivered;
    }
}
