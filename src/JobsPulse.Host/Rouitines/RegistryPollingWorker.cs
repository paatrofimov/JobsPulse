using JobsPulse.Core.Options;
using JobsPulse.Core.Pipeline;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Host.Rouitines;

public sealed class RegistryPollingWorker(
    RegistryPollingService registryPolling,
    IOptionsMonitor<RegistryPollingOptions> options,
    ILog log) : BackgroundService
{
    private readonly ILog ctxLog = log.ForContext<RegistryPollingWorker>();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.CurrentValue.Enabled)
        {
            ctxLog.Info("Registry polling is disabled (RegistryPolling:Enabled=false)");
            return;
        }

        try
        {
            // The watchlist cycle and the discovery bootstrap go first.
            await Task.Delay(TimeSpan.FromMinutes(options.CurrentValue.StartDelayMinutes), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await registryPolling.TryRunCycleAsync(stoppingToken);

                if (!result.Started)
                    ctxLog.Debug("Registry cycle is skipped — the previous one is still running");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                ctxLog.Debug("Gracefully finished with cancellation");
                break;
            }
            catch (Exception ex)
            {
                ctxLog.Error(ex, "Registry polling cycle finished with error");
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromMinutes(options.CurrentValue.CycleIntervalMinutes),
                    stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
