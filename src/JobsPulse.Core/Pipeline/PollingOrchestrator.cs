using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Core.Options;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Core.Pipeline;

public sealed class PollingOrchestrator(
    IWatchlistProvider watchlistProvider,
    EntryProcessor entryProcessor,
    IOptionsMonitor<WatchlistPollingOptions> options,
    TimeProvider clock,
    ILog log)
{
    private readonly ILog ctxLog = log.ForContext<PollingOrchestrator>();
    private readonly Dictionary<string, DateTimeOffset> _lastRunByEntry = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim cycleGate = new(1, 1);

    public async Task<CycleReport> RunCycleAsync(CancellationToken ct)
    {
        // Cycles never overlap: a forced wake-up arriving mid-cycle waits for the running one to finish.
        await cycleGate.WaitAsync(ct);
        try
        {
            return await RunCycleCoreAsync(force: false, ct);
        }
        finally
        {
            cycleGate.Release();
        }
    }

    /// <summary>Runs a forced cycle over every enabled entry, only if none is in progress.</summary>
    public async Task<CycleRunResult> TryRunCycleAsync(CancellationToken ct)
    {
        if (!await cycleGate.WaitAsync(0, ct))
            return CycleRunResult.Busy;

        try
        {
            return CycleRunResult.Completed(await RunCycleCoreAsync(force: true, ct));
        }
        finally
        {
            cycleGate.Release();
        }
    }

    private async Task<CycleReport> RunCycleCoreAsync(bool force, CancellationToken ct)
    {
        var opts = options.CurrentValue;
        var current = watchlistProvider.Current;
        var now = clock.GetUtcNow();

        // A forced cycle ignores the scheduling state entirely -- every enabled entry is processed as on start-up.
        var due = force
            ? current.Entries.Where(e => e.Enabled).ToList()
            : current.Entries.Where(e => e.Enabled && IsDue(e, opts, now)).ToList();

        if (due.Count == 0)
        {
            ctxLog.Debug("No records for traversal (watchlist total: {Total})", current.Entries.Count);
            return CycleReport.Empty;
        }

        ctxLog.Info("Start cycle: {Due} out of {Total} records to traverse", due.Count, current.Entries.Count);

        using var gate = new SemaphoreSlim(opts.MaxConcurrentEntries);

        var results = await Task.WhenAll(due.Select(async entry =>
        {
            await gate.WaitAsync(ct);
            try
            {
                return await ProcessEntryAsync(entry, current, opts, ct);
            }
            finally
            {
                gate.Release();
            }
        }));

        foreach (var entry in due)
            _lastRunByEntry[entry.Id] = now;

        var report = CycleReport.Aggregate(results);
        ctxLog.Info(
            "Cycle finished: boards {Boards}, fetched vacancies {Fetched}, matched vacancies {Matched}, changes {Changes}, errors {Failed}",
            report.BoardsProcessed, report.VacanciesFetched, report.VacanciesMatched, report.Changes, report.Failed);

        return report;
    }

    private async Task<EntryReport> ProcessEntryAsync(
        WatchEntry entry, Watchlist config, WatchlistPollingOptions opts, CancellationToken ct)
    {
        var result = await entryProcessor.ProcessAsync(
            entry,
            entry.CustomFilter ?? config.DefaultFilter,
            new EntryProcessSettings(opts.SingleEntryProcessTimeoutSeconds, opts.DryRun),
            ct);

        if (result.BoardMissing)
        {
            ctxLog.Warn("Board {Board} ({Company}) not found — disabling watchlist entry {Id}", entry.BoardId, entry.CompanyName, entry.Id);
            await watchlistProvider.SetEnabledAsync(entry.Id, false, ct);
        }

        return result.Report;
    }

    private bool IsDue(WatchEntry entry, WatchlistPollingOptions opts, DateTimeOffset now)
    {
        var interval = TimeSpan.FromMinutes(entry.IntervalMinutesOverride ?? opts.PollingIntervalMinutes);
        return !_lastRunByEntry.TryGetValue(entry.Id, out var last) || now - last >= interval;
    }
}

public readonly record struct EntryReport(int Fetched, int Matched, int Changes, bool Failed)
{
    public static EntryReport Failure() => new(0, 0, 0, true);
}

public readonly record struct CycleReport(
    int BoardsProcessed,
    int VacanciesFetched,
    int VacanciesMatched,
    int Changes,
    int Failed)
{
    public static readonly CycleReport Empty = new(0, 0, 0, 0, 0);

    public static CycleReport Aggregate(IReadOnlyList<EntryReport> entries) => new(
        entries.Count,
        entries.Sum(e => e.Fetched),
        entries.Sum(e => e.Matched),
        entries.Sum(e => e.Changes),
        entries.Count(e => e.Failed));
}