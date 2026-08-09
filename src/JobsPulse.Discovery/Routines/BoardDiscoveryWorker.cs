using JobsPulse.Core.Abstractions;
using JobsPulse.Discovery.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Discovery.Routines;

public sealed class BoardDiscoveryWorker(
    IBoardDiscoveryService discovery,
    IBoardRegistryStorage registry,
    IOptionsMonitor<DiscoveryOptions> options,
    ILog log) : BackgroundService
{
    private readonly ILog ctxLog = log.ForContext<BoardDiscoveryWorker>();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.CurrentValue.Enabled)
        {
            ctxLog.Info("Board discovery is disabled (Discovery:Enabled=false)");
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromMinutes(options.CurrentValue.StartDelayMinutes), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // An empty registry means the bootstrap has never run - only then the whole history is worth reading.
        var counts = await registry.CountBySourceAsync(stoppingToken);
        var full = counts.Values.Sum() == 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var report = await discovery.RunAsync(full, stoppingToken);

                if (!report.Started)
                    ctxLog.Info("Discovery run is skipped — another one is in progress");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                ctxLog.Debug("Gracefully finished with cancellation");
                break;
            }
            catch (Exception ex)
            {
                ctxLog.Error(ex, "Board discovery iteration has failed");
            }

            // Everything after the bootstrap is incremental: only crawl indexes that were never processed.
            full = false;

            try
            {
                await Task.Delay(
                    TimeSpan.FromHours(Math.Max(1, options.CurrentValue.RunIntervalHours)),
                    stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
