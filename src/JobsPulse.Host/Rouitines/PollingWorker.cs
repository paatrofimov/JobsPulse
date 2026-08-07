using JobsPulse.Core.Options;
using JobsPulse.Core.Pipeline;
using Microsoft.Extensions.Options;

namespace JobsPulse.Host.Rouitines;

public sealed class PollingWorker(
    PollingOrchestrator orchestrator,
    IOptionsMonitor<WatchlistPollingOptions> options,
    ILogger<PollingWorker> log
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.CurrentValue.DryRun)
            log.LogWarning("DRY-RUN");

        while (!stoppingToken.IsCancellationRequested)
        {
            var period = TimeSpan.FromMinutes(Math.Max(1, options.CurrentValue.PollingIntervalMinutes));

            try
            {
                await orchestrator.RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                log.LogDebug("Gracefully finished with cancellation");
                break;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Polling cycle finished with error");
            }

            try
            {
                await Task.Delay(period, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                log.LogDebug("Gracefully finished with cancellation");
                break;
            }
        }
    }
}