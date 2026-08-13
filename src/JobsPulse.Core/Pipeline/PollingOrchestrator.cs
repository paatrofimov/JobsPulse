using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Core.Options;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Core.Pipeline;

/// <summary>
/// The priority cycle: every board of every enabled watchlist. Boards shared by several watchlists are fetched once
/// and evaluated against each of their filters, so adding a board to a second watchlist costs no extra traffic.
/// </summary>
public sealed class PollingOrchestrator(
    IWatchlistStorage watchlists,
    BoardProcessor boardProcessor,
    ITraversalProgressTracker progress,
    IOptionsMonitor<WatchlistPollingOptions> options,
    TimeProvider clock,
    ILog log)
{
    private readonly ILog ctxLog = log.ForContext<PollingOrchestrator>();

    /// <summary>Scheduling state is per board, not per watchlist entry - the fetch is what has to be throttled.</summary>
    private readonly Dictionary<string, DateTimeOffset> lastRunByBoard = new(StringComparer.OrdinalIgnoreCase);

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

    /// <summary>Runs a forced cycle over every board of every enabled watchlist, only if none is in progress.</summary>
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
        var plan = WatchlistPlan.Build(await watchlists.GetEnabledAsync(ct));
        var now = clock.GetUtcNow();

        if (plan.Boards.Count == 0)
        {
            ctxLog.Debug("No boards to traverse — no enabled watchlist has entries");

            // An empty cycle still closes the progress: a watchlist emptied by hand must not read as «still running».
            progress.CycleFinished(TraversalKind.Watchlist, []);

            return CycleReport.Empty;
        }

        // A forced cycle ignores the scheduling state entirely -- every board is processed as on start-up.
        var due = force
            ? plan.Boards
            : plan.Boards.Where(b => IsDue(b, opts, now)).ToList();

        if (due.Count == 0)
        {
            ctxLog.Debug("No boards are due for traversal (watchlist boards total: {Total})", plan.Boards.Count);
            progress.CycleFinished(TraversalKind.Watchlist, Coverage(plan.Boards, []));

            return CycleReport.Empty;
        }

        progress.CycleStarted(TraversalKind.Watchlist, Coverage(plan.Boards, due));

        ctxLog.Info(
            "Start cycle: {Due} out of {Total} boards to traverse, {Filters} watchlist filters",
            due.Count, plan.Boards.Count, plan.StorageFilters.Count);

        var settings = new BoardProcessSettings(
            opts.SingleEntryProcessTimeoutSeconds,
            opts.DryRun,
            plan.StorageFilters,
            plan.StorageFilterHash);

        using var gate = new SemaphoreSlim(opts.MaxConcurrentEntries);

        var results = await Task.WhenAll(due.Select(async board =>
        {
            await gate.WaitAsync(ct);
            try
            {
                return await ProcessBoardAsync(board, settings, ct);
            }
            finally
            {
                gate.Release();
            }
        }));

        foreach (var board in due)
            lastRunByBoard[board.BoardKey] = now;

        // Coverage is reported after the stamps are written, so it is the post-cycle truth.
        progress.CycleFinished(TraversalKind.Watchlist, Coverage(plan.Boards, []));

        var report = CycleReport.Aggregate(results);
        ctxLog.Info(
            "Cycle finished: boards {Boards}, fetched vacancies {Fetched}, watchlist matches {Matched}, changes {Changes}, errors {Failed}",
            report.BoardsProcessed, report.VacanciesFetched, report.VacanciesMatched, report.Changes, report.Failed);

        return report;
    }

    private async Task<BoardReport> ProcessBoardAsync(
        BoardWorkItem board,
        BoardProcessSettings settings,
        CancellationToken ct)
    {
        var result = await boardProcessor.ProcessAsync(board, settings, ct);

        progress.UnitFinished(
            TraversalKind.Watchlist, board.SourceId, result.BoardMissing || result.Report.Failed);

        if (result.BoardMissing)
        {
            // The board is dead for everybody, not just for one watchlist.
            var disabled = await watchlists.DisableBoardAsync(board.SourceId, board.BoardId, ct);

            ctxLog.Warn(
                "Board {Board} ({Company}) not found — {Count} watchlist entries disabled",
                board.BoardKey, board.CompanyName, disabled);
        }

        return result.Report;
    }

    /// <summary>
    /// The per-source progress units of the cycle: the whole watchlist board set as the dataset, the boards that
    /// already carry a run stamp as its covered part, and the due ones as the plan of this cycle.
    /// </summary>
    private List<TraversalSourceUnits> Coverage(
        IReadOnlyList<BoardWorkItem> boards,
        IReadOnlyList<BoardWorkItem> due)
    {
        var planned = due
            .GroupBy(b => b.SourceId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        return
        [
            .. boards
                .GroupBy(b => b.SourceId, StringComparer.OrdinalIgnoreCase)
                .Select(g => new TraversalSourceUnits
                {
                    SourceId = g.Key,
                    Planned = planned.GetValueOrDefault(g.Key),
                    DatasetTotal = g.Count(),
                    DatasetCovered = g.Count(b => lastRunByBoard.ContainsKey(b.BoardKey))
                })
        ];
    }

    private bool IsDue(BoardWorkItem board, WatchlistPollingOptions opts, DateTimeOffset now)
    {
        var interval = TimeSpan.FromMinutes(board.IntervalMinutesOverride ?? opts.PollingIntervalMinutes);
        return !lastRunByBoard.TryGetValue(board.BoardKey, out var last) || now - last >= interval;
    }
}
