using JobsPulse.Core.Options;
using JobsPulse.Host.Pipeline;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Host.Routines;

public sealed class OutboxDispatcher(
    OutboxDelivery delivery,
    IOptionsMonitor<DeliveryOptions> deliveryOptions,
    ILog log) : BackgroundService
{
    private readonly ILog ctxLog = log.ForContext<OutboxDispatcher>();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await delivery.DispatchOnceAsync(stoppingToken);
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
                await Task.Delay(TimeSpan.FromSeconds(deliveryOptions.CurrentValue.DispatchOutboxIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                ctxLog.Debug("Gracefully finished with cancellation");
                break;
            }
        }
    }
}
