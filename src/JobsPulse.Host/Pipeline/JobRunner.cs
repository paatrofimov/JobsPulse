using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Infrastructure;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Core.Options;
using JobsPulse.Core.Pipeline;
using JobsPulse.Discovery.Options;
using JobsPulse.Discovery.Pipeline;
using JobsPulse.Host.Models;
using JobsPulse.Host.Options;
using JobsPulse.Sinks.Telegram.Infrastructure;
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
    DigestService digests,
    RunReportService runReports,
    WatchlistHistoryRepair historyRepair,
    ITraversalRunStorage runs,
    CurrentTraversalRun currentRun,
    WebhookRegistrar webhookRegistrar,
    IOptionsMonitor<WatchlistPollingOptions> pollingOptions,
    IOptionsMonitor<RegistryPollingOptions> registryOptions,
    IOptionsMonitor<DiscoveryOptions> discoveryOptions,
    IOptionsMonitor<JobOptions> jobOptions,
    IOptionsMonitor<DeliveryOptions> deliveryOptions,
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
        var startedAt = DateTimeOffset.UtcNow;
        var outcome = new JobOutcome();

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

        if (opts.MaxRunMinutes > 0)
            deadline.CancelAfter(TimeSpan.FromMinutes(opts.MaxRunMinutes));

        ctxLog.Info("Job {Role} has started", role);

        var delivers = role is HostRole.Polling or HostRole.Registry;

        // The run row tells every dispatcher - this one and the other job's - that this job is still filling the open
        // window, from the first slice to the last one, gaps between slices included.
        var run = delivers
            ? await StartRunAsync(role, stoppingToken)
            : null;

        // Every commit of this process is stamped with the run, which is what the run report counts.
        currentRun.Id = run;

        using var stopHeartbeat = new CancellationTokenSource();
        var heartbeat = run is { } runId
            ? HeartbeatLoopAsync(runId, opts, stopHeartbeat.Token)
            : Task.CompletedTask;

        // Every closed delivery window leaves while the cycle is still walking instead of the whole cycle arriving at
        // its end.
        using var stopDispatch = new CancellationTokenSource();
        var dispatch = delivers
            ? DispatchLoopAsync(stopDispatch.Token, stoppingToken)
            : Task.CompletedTask;

        try
        {
            await RunRoleAsync(role, outcome, deadline.Token);
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

        // The walk is over: the drain below sends the open window, unless the other job is still filling it.
        await stopHeartbeat.CancelAsync();
        await heartbeat;

        if (run is { } finishedId)
            await FinishRunAsync(finishedId);

        currentRun.Id = null;

        await stopDispatch.CancelAsync();
        await dispatch;

        // Changes committed before a failure or a deadline are delivered all the same.
        if (delivers && !stoppingToken.IsCancellationRequested)
            exitCode = Math.Max(exitCode, await DrainAsync(opts, stoppingToken));

        // After the drain, so the report arrives after the changes it counts rather than in the middle of them.
        if (!stoppingToken.IsCancellationRequested && !outcome.Skipped)
            await SendRunReportAsync(role, run, startedAt, outcome, stoppingToken);

        ctxLog.Info("Job {Role} has finished with exit code {ExitCode}", role, exitCode);

        return exitCode;
    }

    private async Task RunRoleAsync(HostRole role, JobOutcome outcome, CancellationToken ct)
    {
        switch (role)
        {
            case HostRole.Polling:
                await RunPollingAsync(outcome, ct);
                break;
            case HostRole.Registry:
                await RunRegistryAsync(outcome, ct);
                break;
            case HostRole.Discovery:
                await RunDiscoveryAsync(outcome, ct);
                break;
            case HostRole.WebhookSetup:
                await webhookRegistrar.RegisterAsync(ct);
                break;
            case HostRole.Cleanup:
                await outbox.PurgeDeliveredAsync(ct);
                break;
            case HostRole.Digest:
                await digests.SendAsync(ct);
                break;
            case HostRole.HistoryRepair:
                await historyRepair.RunAsync(ct);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(role), role, "Role is not a one-shot job");
        }
    }

    private async Task RunPollingAsync(JobOutcome outcome, CancellationToken ct)
    {
        if (pollingOptions.CurrentValue.DryRun)
            ctxLog.Warn("DRY-RUN");

        // Stored vacancies are re-evaluated first, so the cycle works against the current filter.
        await filterMaintenance.RunAsync(ct);
        outcome.Cycle = await orchestrator.RunCycleAsync(ct);
    }

    private async Task RunRegistryAsync(JobOutcome outcome, CancellationToken ct)
    {
        if (!registryOptions.CurrentValue.Enabled)
        {
            ctxLog.Info("Registry polling is disabled (RegistryPolling:Enabled=false)");
            outcome.Skipped = true;
            return;
        }

        // Without a time box there is no budget to fill, so the job stays a single slice.
        var minutes = jobOptions.CurrentValue.MaxRunMinutes;
        var result = minutes > 0
            ? await registryPolling.TryRunSweepAsync(DateTimeOffset.UtcNow.AddMinutes(minutes), ct)
            : await registryPolling.TryRunCycleAsync(ct);

        outcome.Skipped = !result.Started;
        outcome.Cycle = result.Report;
    }

    private async Task RunDiscoveryAsync(JobOutcome outcome, CancellationToken ct)
    {
        if (!discoveryOptions.CurrentValue.Enabled)
        {
            ctxLog.Info("Board discovery is disabled (Discovery:Enabled=false)");
            outcome.Skipped = true;
            return;
        }

        var full = await bootstrapPolicy.IsBootstrapDueAsync(ct);
        outcome.DiscoveryFull = full;

        var report = await discovery.RunAsync(full, ct);
        outcome.Skipped = !report.Started;
        outcome.Discovery = report;

        if (report.CollectionsPending > 0)
            ctxLog.Warn(
                "{Pending} crawl collections are left pending ({Failed} failed) — the next run continues from them",
                report.CollectionsPending, report.CollectionsFailed);
    }

    /// <summary>
    /// The <see cref="Routines.OutboxDispatcher"/> loop, run next to the cycle. <paramref name="stop"/> only ends the
    /// loop between ticks - a delivery in flight is finished, not cancelled, so a sent batch is always marked as sent.
    /// </summary>
    private async Task DispatchLoopAsync(CancellationToken stop, CancellationToken stoppingToken)
    {
        while (!stop.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
        {
            try
            {
                await outbox.DispatchOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                ctxLog.Error(ex, "Outbox dispatch iteration has failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(deliveryOptions.CurrentValue.DispatchOutboxIntervalSeconds), stop);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Null when the row cannot be written - the job still runs, the dispatchers just cannot see it.</summary>
    private async Task<long?> StartRunAsync(HostRole role, CancellationToken ct)
    {
        try
        {
            return await runs.StartAsync(role.ToString(), ct);
        }
        catch (Exception ex)
        {
            ctxLog.Warn(ex, "Job {Role} could not register its run — other jobs may send the open window early", role);
            return null;
        }
    }

    private async Task HeartbeatLoopAsync(long runId, JobOptions opts, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, opts.HeartbeatSeconds)), stop);
                await runs.HeartbeatAsync(runId, stop);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                ctxLog.Warn(ex, "Heartbeat of run {Run} has failed", runId);
            }
        }
    }

    /// <summary>Not cancellable: a stopped job must still say it is no longer walking, or it holds windows back until its heartbeat goes stale.</summary>
    private async Task FinishRunAsync(long runId)
    {
        try
        {
            await runs.FinishAsync(runId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            ctxLog.Warn(ex, "Run {Run} could not be finished — it stops counting once its heartbeat is stale", runId);
        }
    }

    /// <summary>A failed report never fails the job - it is only logged.</summary>
    private async Task SendRunReportAsync(
        HostRole role,
        long? runId,
        DateTimeOffset startedAt,
        JobOutcome outcome,
        CancellationToken stoppingToken)
    {
        try
        {
            switch (role)
            {
                case HostRole.Polling:
                    await runReports.SendTraversalAsync(
                        TraversalKind.Watchlist, runId, startedAt, outcome.Cycle, stoppingToken);
                    break;
                case HostRole.Registry:
                    await runReports.SendTraversalAsync(
                        TraversalKind.Registry, runId, startedAt, outcome.Cycle, stoppingToken);
                    break;
                case HostRole.Discovery:
                    await runReports.SendDiscoveryAsync(
                        startedAt, outcome.DiscoveryFull, outcome.Discovery, stoppingToken);
                    break;
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            ctxLog.Warn("Run report of job {Role} is stopped by the host", role);
        }
        catch (Exception ex)
        {
            ctxLog.Error(ex, "Run report of job {Role} has failed", role);
        }
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
