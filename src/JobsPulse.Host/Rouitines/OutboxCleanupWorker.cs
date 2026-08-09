using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Options;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Host.Rouitines;

public sealed class OutboxCleanupWorker(
    IOutboxStorage outboxStorage,
    IOptionsMonitor<DeliveryOptions> options,
    TimeProvider clock,
    ILog log) : BackgroundService
{
    private readonly ILog ctxLog = log.ForContext<OutboxCleanupWorker>();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var opts = options.CurrentValue;

            try
            {
                var threshold = clock.GetUtcNow().AddHours(-opts.DeliveredRetentionHours);
                var deleted = await outboxStorage.PurgeDeliveredAsync(threshold, stoppingToken);

                if (deleted > 0)
                    ctxLog.Info("Removed {Deleted} delivered outbox notifications older than {Threshold}", deleted, threshold);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                ctxLog.Debug("Gracefully finished with cancellation");
                break;
            }
            catch (Exception ex)
            {
                ctxLog.Error(ex, "Outbox cleanup iteration has failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(opts.CleanupIntervalMinutes), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
