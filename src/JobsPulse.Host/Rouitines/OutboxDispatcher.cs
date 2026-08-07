using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Options;
using Microsoft.Extensions.Options;

namespace JobsPulse.Host.Rouitines;

public sealed class OutboxDispatcher(
    IOutboxStorage outboxStorage,
    IVacancySink sink,
    IOptionsMonitor<DeliveryOptions> deliveryOptions,
    ILogger<OutboxDispatcher> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var opts = deliveryOptions.CurrentValue;

            try
            {
                await outboxStorage.MarkAsDeadLetterAsync(opts.MaxAttemptsBeforeDeadLetter, stoppingToken);

                var batch = await outboxStorage.ReadAndLeaseAsync(opts.OutboxBatchSize, stoppingToken);

                if (batch.Count > 0)
                    await DeliverBatchAsync(batch, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                log.LogDebug("Gracefully finished with cancellation");
                break;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Outbox dispatcher iteration fail");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(opts.DispatchOutboxIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                log.LogDebug("Gracefully finished with cancellation");
                break;
            }
        }
    }

    private async Task DeliverBatchAsync(IReadOnlyList<OutboxItem> items, CancellationToken ct)
    {
        // todo (patrofimov) when delivery throws, 'leased' letters are not rescheduled as pending
        // maybe use disposable lease and reset status on error

        var ids = items.Select(i => i.Id).ToList();
        var result = await sink.DeliverAsync(items, ct);

        if (result.Success)
        {
            await outboxStorage.MarkSentAsync(ids, ct);
            log.LogInformation("Sent {Count} messages", items.Count);
            return;
        }

        // If telegram reponses with desired timeout - use it; otherwise, use exponential backoff
        var attempts = items.Max(i => i.Attempts);
        var backoff = result.RetryAfter ?? TimeSpan.FromSeconds(Math.Min(300, Math.Pow(2, attempts + 1)));

        await outboxStorage.MarkFailedAsync(ids, backoff, result.Error ?? "unknown", ct);
        log.LogWarning("Delivery has failed ({Error}), will retry after {Backoff}",
            result.Error, backoff);
    }
}