using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Infrastructure;
using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Core.Options;
using JobsPulse.Host.Models;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Host.Pipeline;

public sealed class OutboxDelivery(
    IOutboxStorage outboxStorage,
    IVacancySink sink,
    ITraversalProgressTracker progress,
    IOptionsMonitor<DeliveryOptions> deliveryOptions,
    TimeProvider clock,
    ILog log)
{
    private readonly ILog ctxLog = log.ForContext<OutboxDelivery>();

    /// <summary>One dispatcher tick: dead-letters exhausted items, then leases and sends one batch.</summary>
    public async Task<OutboxDispatchResult> DispatchOnceAsync(CancellationToken ct)
    {
        var opts = deliveryOptions.CurrentValue;

        await outboxStorage.MarkAsDeadLetterAsync(opts.MaxAttemptsBeforeDeadLetter, ct);

        var cutoff = await CutoffAsync(opts, ct);

        var batch = await outboxStorage.ReadAndLeaseAsync(opts.OutboxBatchSize, cutoff, ct);

        if (batch.Count == 0)
            return OutboxDispatchResult.Idle;

        return await DeliverBatchAsync(batch, ct);
    }

    /// <summary>
    /// Sends everything that is allowed to leave, batch after batch. Used by one-shot jobs after their cycle: the
    /// traversal is over, so the cutoff is «now» and every window goes out whole. A failure with a short retry
    /// hint is waited out; a longer one is left to the next run.
    /// </summary>
    public async Task DrainAsync(TimeSpan maxRetryAfter, CancellationToken ct)
    {
        var total = 0;

        while (true)
        {
            var result = await DispatchOnceAsync(ct);
            total += result.Delivered;

            if (result.RetryAfter is { } retryAfter)
            {
                if (retryAfter > maxRetryAfter)
                {
                    ctxLog.Warn("Outbox drain is stopped: retry is due in {RetryAfter} — the next run sends the rest", retryAfter);
                    break;
                }

                await Task.Delay(retryAfter, ct);
                continue;
            }

            if (result.Delivered == 0)
                break;
        }

        ctxLog.Info("Outbox is drained: {Total} notifications sent", total);
    }

    /// <summary>Deletes delivered rows older than <c>Delivery:DeliveredRetentionHours</c>.</summary>
    public async Task<int> PurgeDeliveredAsync(CancellationToken ct)
    {
        var threshold = clock.GetUtcNow().AddHours(-deliveryOptions.CurrentValue.DeliveredRetentionHours);
        var deleted = await outboxStorage.PurgeDeliveredAsync(threshold, ct);

        if (deleted > 0)
            ctxLog.Info("Removed {Deleted} delivered outbox notifications older than {Threshold}", deleted, threshold);

        return deleted;
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

    private async Task<OutboxDispatchResult> DeliverBatchAsync(IReadOnlyList<OutboxItem> items, CancellationToken ct)
    {
        var ids = items.Select(i => i.Id).ToList();
        var attempts = items.Max(i => i.Attempts);

        DeliveryResult result;

        try
        {
            result = await sink.DeliverAsync(items, ct);
        }
        catch (Exception ex)
        {
            // A leased item is never picked up again, so a throwing sink must hand the batch back as pending -
            // with CancellationToken.None, because a shutdown is exactly when this has to be written.
            var retry = ex is OperationCanceledException ? TimeSpan.Zero : Backoff(attempts);
            await outboxStorage.MarkFailedAsync(ids, retry, ex.Message, CancellationToken.None);

            if (ex is OperationCanceledException)
                throw;

            ctxLog.Error(ex, "Delivery has thrown, will retry after {Backoff}", retry);
            return new OutboxDispatchResult(0, retry);
        }

        if (result.Success)
        {
            await outboxStorage.MarkDeliveredAsync(ids, ct);
            ctxLog.Info("Sent {Count} messages", items.Count);
            return new OutboxDispatchResult(items.Count, null);
        }

        // If telegram reponses with desired timeout - use it; otherwise, use exponential backoff
        var backoff = result.RetryAfter ?? Backoff(attempts);

        await outboxStorage.MarkFailedAsync(ids, backoff, result.Error ?? "unknown", ct);
        ctxLog.Warn("Delivery has failed ({Error}), will retry after {Backoff}", result.Error, backoff);

        return new OutboxDispatchResult(0, backoff);
    }

    private static TimeSpan Backoff(int attempts)
    {
        return TimeSpan.FromSeconds(Math.Min(300, Math.Pow(2, attempts + 1)));
    }
}
