using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Core.Options;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Core.Pipeline;

/// <summary>
/// Sends every enabled watchlist with at least one company what changed since its previous delivered digest - the
/// first one covers the last <c>Digest:PeriodDays</c>. A digest with nothing in it is not sent. Run by `--role digest`;
/// how often is decided by whoever starts the job (cron-job.org), not here.
/// </summary>
public sealed class DigestService(
    IWatchlistStorage watchlists,
    IWatchlistDigestStorage digests,
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
        var options = digestOptions.CurrentValue;

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
            var to = clock.GetUtcNow();

            // A digest continues the last one that arrived; a gap longer than MaxPeriodDays is cut, as on request.
            var previous = await digests.GetLastDeliveredAsync(watchlist.Id, ct);
            var from = previous is null
                ? to.AddDays(-options.PeriodDays)
                : Max(previous.To, to.AddDays(-options.MaxPeriodDays));

            var computed = await stats.ComputeAsync(watchlist, from, to, ct);

            // Nothing is stored either, so the next digest starts at the same moment and covers the quiet stretch too.
            if (IsEmpty(computed))
            {
                ctxLog.Info("Digest of watchlist {Watchlist} is empty - not sent", watchlist.Id);
                continue;
            }

            var digest = await digests.AddAsync(watchlist.Id, from, to, previous?.Id, ct);
            var result = await sink.DeliverDigestAsync(watchlist, computed, digest, ct);

            if (result.Success)
            {
                await digests.MarkDeliveredAsync(digest.Id, clock.GetUtcNow(), ct);
                delivered++;
            }
            else
            {
                ctxLog.Warn("Digest of watchlist {Watchlist} was not delivered: {Error}", watchlist.Id, result.Error);
            }
        }

        ctxLog.Info("Delivered {Delivered} of {Total} digests", delivered, targets.Count);

        return delivered;
    }

    /// <summary>Nothing opened or closed, and nothing aged out or was filtered away either - the open counts held.</summary>
    private static bool IsEmpty(WatchlistStats stats) =>
        stats is { Opened: 0, Closed: 0, Dropped: 0 } && stats.OpenAtStart == stats.OpenAtEnd;

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) =>
        a > b ? a : b;
}
