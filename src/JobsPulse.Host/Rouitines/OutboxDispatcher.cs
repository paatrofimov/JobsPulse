using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Infrastructure;
using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Options;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Host.Rouitines;

public sealed class OutboxDispatcher(
    IOutboxStorage outboxStorage,
    IVacancySink sink,
    ITraversalProgressTracker progress,
    IOptionsMonitor<DeliveryOptions> deliveryOptions,
    TimeProvider clock,
    ILog log) : BackgroundService
{
    private readonly ILog ctxLog = log.ForContext<OutboxDispatcher>();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var opts = deliveryOptions.CurrentValue;

            try
            {
                await outboxStorage.MarkAsDeadLetterAsync(opts.MaxAttemptsBeforeDeadLetter, stoppingToken);

                var cutoff = await CutoffAsync(opts, stoppingToken);

                var batch = await outboxStorage.ReadAndLeaseAsync(opts.OutboxBatchSize, cutoff, stoppingToken);

                if (batch.Count > 0)
                    await DeliverBatchAsync(batch, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                ctxLog.Debug("Gracefully finished with cancellation");
                break;
            }
            catch (Exception ex)
            {
                ctxLog.Error(ex, "Outbox dispatcher iteration fail");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(opts.DispatchOutboxIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                ctxLog.Debug("Gracefully finished with cancellation");
                break;
            }
        }
    }

    /// <summary>
    /// Everything enqueued before this instant may be sent; the rest belongs to the delivery window still being
    /// filled and waits. Without the cutoff the dispatcher drained the outbox every few seconds, and because a
    /// traversal commits per board, a window that should have been one message arrived as one message per company.
    ///
    /// Three things open the gate, which is why this is not a plain «wait five minutes»:
    /// - the window closed - the ordinary case, and the reason messages are aligned to five minute ranges;
    /// - every traversal is idle - the walk is over, so nothing more can land in the open window and holding it
    ///   back would only delay the report;
    /// - the open window already holds more changes than one message can carry - there is nothing left to group.
    /// </summary>
    private async Task<DateTimeOffset> CutoffAsync(DeliveryOptions opts, CancellationToken ct)
    {
        var now = clock.GetUtcNow();

        // Nothing is walking, so the open window is complete by definition.
        if (progress.Snapshot().All(traversal => !traversal.IsRunning))
            return now;

        var pending = await outboxStorage.CountPendingAsync(ct);

        if (pending >= opts.FlushWindowAfterChanges)
        {
            ctxLog.Info(
                "{Pending} changes are waiting while a traversal runs — sending the open window without waiting "
                + "for it to close",
                pending);

            return now;
        }

        return DeliveryWindow.Floor(now, DeliveryWindow.Of(opts.GroupChangesWithinMinutes));
    }

    private async Task DeliverBatchAsync(IReadOnlyList<OutboxItem> items, CancellationToken ct)
    {
        // todo (patrofimov) when delivery throws, 'leased' letters are not rescheduled as pending
        // maybe use disposable lease and reset status on error

        var ids = items.Select(i => i.Id).ToList();
        var result = await sink.DeliverAsync(items, ct);

        if (result.Success)
        {
            await outboxStorage.MarkDeliveredAsync(ids, ct);
            ctxLog.Info("Sent {Count} messages", items.Count);
            return;
        }

        // If telegram reponses with desired timeout - use it; otherwise, use exponential backoff
        var attempts = items.Max(i => i.Attempts);
        var backoff = result.RetryAfter ?? TimeSpan.FromSeconds(Math.Min(300, Math.Pow(2, attempts + 1)));

        await outboxStorage.MarkFailedAsync(ids, backoff, result.Error ?? "unknown", ct);
        ctxLog.Warn("Delivery has failed ({Error}), will retry after {Backoff}",
            result.Error, backoff);
    }
}