using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Core.Options;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Core.Pipeline;

/// <summary>
/// Secondary polling cycle over the discovered board registry. The watchlist cycle stays the priority feed -
/// this one walks the registry round-robin, a slice per cycle, with its own throttling.
///
/// Registry boards belong to no watchlist, so they never notify: the cycle only keeps the global vacancy state warm
/// (which is what makes /boards useful) and deactivates boards that stopped answering.
/// </summary>
public sealed class RegistryPollingService(
    IBoardRegistryStorage registry,
    IWatchlistStorage watchlists,
    BoardProcessor boardProcessor,
    IOptionsMonitor<RegistryPollingOptions> options,
    ILog log)
{
    private readonly ILog ctxLog = log.ForContext<RegistryPollingService>();
    private readonly SemaphoreSlim cycleGate = new(1, 1);

    /// <summary>Round-robin cursor over the registry. In-memory: after a restart the walk simply starts over.</summary>
    private int cursor;

    public async Task<CycleRunResult> TryRunCycleAsync(CancellationToken ct)
    {
        if (!await cycleGate.WaitAsync(0, ct))
            return CycleRunResult.Busy;

        try
        {
            return CycleRunResult.Completed(await RunCycleCoreAsync(ct));
        }
        finally
        {
            cycleGate.Release();
        }
    }

    private async Task<CycleReport> RunCycleCoreAsync(CancellationToken ct)
    {
        var opts = options.CurrentValue;
        var plan = WatchlistPlan.Build(await watchlists.GetEnabledAsync(ct));

        // Without a single enabled watchlist nothing is relevant, so there is nothing to store either.
        if (!plan.HasWatchlists)
        {
            ctxLog.Debug("No enabled watchlists — registry cycle is skipped");
            return CycleReport.Empty;
        }

        // Boards of the watchlists are polled by the priority cycle - polling them twice would only duplicate work.
        var watched = plan.Boards
            .Select(b => b.BoardKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var boards = (await registry.ListAsync(null, opts.MaxRegistryBoards, ct))
            .Where(b => b.IsActive && !watched.Contains($"{b.SourceId}/{b.BoardId}"))
            .ToList();

        if (boards.Count == 0)
        {
            ctxLog.Debug("Board registry has nothing to poll");
            return CycleReport.Empty;
        }

        var slice = TakeSlice(boards, opts.BoardsPerCycle);

        ctxLog.Info(
            "Start registry cycle: {Slice} of {Total} boards (cursor {Cursor})",
            slice.Count, boards.Count, cursor);

        var settings = new BoardProcessSettings(
            opts.SingleEntryProcessTimeoutSeconds,
            opts.DryRun,
            plan.StorageFilters,
            plan.StorageFilterHash);

        using var gate = new SemaphoreSlim(opts.MaxConcurrentBoards);

        var results = await Task.WhenAll(slice.Select(async board =>
        {
            await gate.WaitAsync(ct);
            try
            {
                return await ProcessBoardAsync(board, settings, opts, ct);
            }
            finally
            {
                gate.Release();
            }
        }));

        var report = CycleReport.Aggregate(results);
        ctxLog.Info(
            "Registry cycle finished: boards {Boards}, fetched {Fetched}, stored {Stored}, errors {Failed}",
            report.BoardsProcessed, report.VacanciesFetched, report.VacanciesMatched, report.Failed);

        return report;
    }

    private async Task<BoardReport> ProcessBoardAsync(
        RegisteredBoard board,
        BoardProcessSettings settings,
        RegistryPollingOptions opts,
        CancellationToken ct)
    {
        // No subscriptions: the board is not watched, so the run produces state only.
        var work = new BoardWorkItem
        {
            SourceId = board.SourceId,
            BoardId = board.BoardId,
            CompanyName = board.DisplayName ?? board.BoardId,
            Configuration = board.Configuration
        };

        var result = await boardProcessor.ProcessAsync(work, settings, ct);

        if (result.BoardMissing)
        {
            ctxLog.Info("Registry board {Source}/{Board} is gone — deactivated", board.SourceId, board.BoardId);
            await registry.SetActiveAsync(board.SourceId, board.BoardId, false, ct);
        }

        if (opts.DelayBetweenBoardsMs > 0)
            await Task.Delay(opts.DelayBetweenBoardsMs, ct);

        return result.Report;
    }

    private List<RegisteredBoard> TakeSlice(IReadOnlyList<RegisteredBoard> boards, int size)
    {
        if (cursor >= boards.Count)
            cursor = 0;

        var slice = boards.Skip(cursor).Take(size).ToList();

        // The registry is smaller than one slice - wrap around instead of idling.
        if (slice.Count < size && boards.Count > slice.Count)
            slice.AddRange(boards.Take(size - slice.Count));

        cursor = boards.Count == 0 ? 0 : (cursor + slice.Count) % boards.Count;

        return slice;
    }
}
