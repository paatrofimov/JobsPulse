using JobsPulse.Core.Options;
using JobsPulse.Core.Pipeline;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Host.Rouitines;

public sealed class PollingWorker(
    PollingOrchestrator orchestrator,
    IOptionsMonitor<WatchlistPollingOptions> options,
    ILog log
) : BackgroundService
{
    private readonly ILog ctxLog = log.ForContext<OutboxDispatcher>();
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.CurrentValue.DryRun)
            ctxLog.Warn("DRY-RUN");

        while (!stoppingToken.IsCancellationRequested)
        {
            var period = TimeSpan.FromMinutes(Math.Max(1, options.CurrentValue.PollingIntervalMinutes));

            try
            {
                await orchestrator.RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                ctxLog.Debug("Gracefully finished with cancellation");
                break;
            }
            catch (Exception ex)
            {
                ctxLog.Error(ex, "Polling cycle finished with error");
            }

            try
            {
                await Task.Delay(period, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                ctxLog.Debug("Gracefully finished with cancellation");
                break;
            }
        }
    }
}