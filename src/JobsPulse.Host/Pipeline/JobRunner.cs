using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Options;
using JobsPulse.Core.Pipeline;
using JobsPulse.Discovery.Options;
using JobsPulse.Discovery.Pipeline;
using JobsPulse.Host.Models;
using JobsPulse.Host.Options;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Host.Pipeline;

public sealed class JobRunner(
    PollingOrchestrator orchestrator,
    FilterMaintenanceService filterMaintenance,
    RegistryPollingService registryPolling,
    IBoardDiscoveryService discovery,
    DiscoveryBootstrapPolicy bootstrapPolicy,
    OutboxDelivery outbox,
    IOptionsMonitor<WatchlistPollingOptions> pollingOptions,
    IOptionsMonitor<RegistryPollingOptions> registryOptions,
    IOptionsMonitor<DiscoveryOptions> discoveryOptions,
    IOptionsMonitor<JobOptions> jobOptions,
    ILog log)
{
    private readonly ILog ctxLog = log.ForContext<JobRunner>();

    /// <summary>
    /// Runs a single iteration of the routine behind <paramref name="role"/> and returns the process exit code.
    /// Hitting <c>Job:MaxRunMinutes</c> is not a failure: every routine keeps its progress in the database, so the
    /// next run continues where this one stopped.
    /// </summary>
    public async Task<int> RunAsync(HostRole role, CancellationToken stoppingToken)
    {
        var opts = jobOptions.CurrentValue;
        var exitCode = 0;

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

        if (opts.MaxRunMinutes > 0)
            deadline.CancelAfter(TimeSpan.FromMinutes(opts.MaxRunMinutes));

        ctxLog.Info("Job {Role} has started", role);

        try
        {
            await RunRoleAsync(role, deadline.Token);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            if (stoppingToken.IsCancellationRequested)
                ctxLog.Warn("Job {Role} is stopped by the host", role);
            else
                ctxLog.Warn("Job {Role} has reached Job:MaxRunMinutes ({Minutes}) — the next run continues from the saved state",
                    role, opts.MaxRunMinutes);
        }
        catch (Exception ex)
        {
            ctxLog.Error(ex, "Job {Role} has failed", role);
            exitCode = 1;
        }

        // Changes committed before a failure or a deadline are delivered all the same.
        if (role is HostRole.Polling or HostRole.Registry && !stoppingToken.IsCancellationRequested)
            exitCode = Math.Max(exitCode, await DrainAsync(opts, stoppingToken));

        ctxLog.Info("Job {Role} has finished with exit code {ExitCode}", role, exitCode);

        return exitCode;
    }

    private async Task RunRoleAsync(HostRole role, CancellationToken ct)
    {
        switch (role)
        {
            case HostRole.Polling:
                await RunPollingAsync(ct);
                break;
            case HostRole.Registry:
                await RunRegistryAsync(ct);
                break;
            case HostRole.Discovery:
                await RunDiscoveryAsync(ct);
                break;
            case HostRole.Cleanup:
                await outbox.PurgeDeliveredAsync(ct);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(role), role, "Role is not a one-shot job");
        }
    }

    private async Task RunPollingAsync(CancellationToken ct)
    {
        if (pollingOptions.CurrentValue.DryRun)
            ctxLog.Warn("DRY-RUN");

        // Stored vacancies are re-evaluated first, so the cycle works against the current filter.
        await filterMaintenance.RunAsync(ct);
        await orchestrator.RunCycleAsync(ct);
    }

    private async Task RunRegistryAsync(CancellationToken ct)
    {
        if (!registryOptions.CurrentValue.Enabled)
        {
            ctxLog.Info("Registry polling is disabled (RegistryPolling:Enabled=false)");
            return;
        }

        await registryPolling.TryRunCycleAsync(ct);
    }

    private async Task RunDiscoveryAsync(CancellationToken ct)
    {
        if (!discoveryOptions.CurrentValue.Enabled)
        {
            ctxLog.Info("Board discovery is disabled (Discovery:Enabled=false)");
            return;
        }

        var full = await bootstrapPolicy.IsBootstrapDueAsync(ct);
        var report = await discovery.RunAsync(full, ct);

        if (report.CollectionsPending > 0)
            ctxLog.Warn(
                "{Pending} crawl collections are left pending ({Failed} failed) — the next run continues from them",
                report.CollectionsPending, report.CollectionsFailed);
    }

    private async Task<int> DrainAsync(JobOptions opts, CancellationToken stoppingToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(Math.Max(1, opts.DrainTimeoutMinutes)));

        try
        {
            await outbox.DrainAsync(TimeSpan.FromSeconds(opts.MaxDrainRetryAfterSeconds), timeout.Token);
            return 0;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            ctxLog.Warn("Outbox drain has reached Job:DrainTimeoutMinutes — the next run sends the rest");
            return 0;
        }
        catch (Exception ex)
        {
            ctxLog.Error(ex, "Outbox drain has failed");
            return 1;
        }
    }
}
