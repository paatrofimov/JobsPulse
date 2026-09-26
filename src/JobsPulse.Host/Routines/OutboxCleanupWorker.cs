using JobsPulse.Core.Options;
using JobsPulse.Host.Pipeline;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Host.Routines;

public sealed class OutboxCleanupWorker(
    OutboxDelivery delivery,
    IOptionsMonitor<DeliveryOptions> options,
    ILog log) : BackgroundService
{
    private readonly ILog ctxLog = log.ForContext<OutboxCleanupWorker>();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await delivery.PurgeDeliveredAsync(stoppingToken);
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
                await Task.Delay(TimeSpan.FromMinutes(options.CurrentValue.CleanupIntervalMinutes), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
