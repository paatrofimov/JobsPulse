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
/// A registry board belongs to no watchlist, so the sweep itself never notifies: it keeps the global vacancy state
/// warm (which is what makes /boards useful) and deactivates boards that stopped answering. What it does produce is
/// promotions - a board whose vacancies pass the filter of some watchlist is handed to
/// <see cref="DiscoveredBoardPromoter"/>, added there and reported at once.
/// </summary>
public sealed class RegistryPollingService(
    IBoardRegistryStorage registry,
    IWatchlistStorage watchlists,
    BoardProcessor boardProcessor,
    DiscoveredBoardPromoter promoter,
    ITraversalProgressTracker progress,
    IOptionsMonitor<RegistryPollingOptions> options,
    ILog log)
{
    private readonly ILog ctxLog = log.ForContext<RegistryPollingService>();
    private readonly SemaphoreSlim cycleGate = new(1, 1);

    /// <summary>Round-robin cursor over the registry. In-memory: after a restart the walk simply starts over.</summary>
    private int cursor;

    /// <summary>
    /// Boards of the current walk, per source. Reset when the cursor wraps, so the reported percentage is «how much
    /// of the registry this round has covered» rather than a number that only ever grows.
    /// </summary>
    private readonly Dictionary<string, int> walkedBySource = new(StringComparer.OrdinalIgnoreCase);

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
        var enabled = await watchlists.GetEnabledAsync(ct);
        var plan = WatchlistPlan.Build(enabled);

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
            progress.CycleFinished(TraversalKind.Registry, []);

            return CycleReport.Empty;
        }

        var slice = TakeSlice(boards, opts.BoardsPerCycle);

        progress.CycleStarted(TraversalKind.Registry, Coverage(boards, slice));

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
                return (Board: board, Result: await ProcessBoardAsync(board, settings, opts, ct));
            }
            finally
            {
                gate.Release();
            }
        }));

        foreach (var board in slice)
        {
            walkedBySource[board.SourceId] = walkedBySource.GetValueOrDefault(board.SourceId) + 1;
        }

        progress.CycleFinished(TraversalKind.Registry, Coverage(boards, []));

        // Promotion is database work only, so it runs after the fetch pass: one writer, and the cap is exact.
        var promoted = await PromoteAsync(results, SelectPromotionCandidates(enabled, opts), opts, ct);

        var report = CycleReport.Aggregate([.. results.Select(r => r.Result.Report)]);
        ctxLog.Info(
            "Registry cycle finished: boards {Boards}, fetched {Fetched}, stored {Stored}, errors {Failed}, "
            + "promotions {Promoted}",
            report.BoardsProcessed, report.VacanciesFetched, report.VacanciesMatched, report.Failed, promoted);

        return report;
    }

    /// <summary>
    /// A promotion candidate is an enabled watchlist with a non-empty filter. A watchlist matching everything would
    /// absorb the entire registry, so it is deliberately never filled automatically.
    /// </summary>
    private IReadOnlyList<Watchlist> SelectPromotionCandidates(
        IReadOnlyList<Watchlist> enabled,
        RegistryPollingOptions opts)
    {
        if (!opts.AutoAdd || opts.DryRun)
            return [];

        var candidates = DiscoveredBoardPromoter.SelectCandidates(enabled);

        var skipped = enabled.Count - candidates.Count;
        if (skipped > 0)
        {
            ctxLog.Info(
                "{Skipped} of {Total} enabled watchlists are not filled from discovery — "
                + "their filter matches everything",
                skipped, enabled.Count);
        }

        return candidates;
    }

    /// <summary>Adds every board that matched a candidate watchlist, up to the per-cycle cap.</summary>
    private async Task<int> PromoteAsync(
        IReadOnlyList<(RegisteredBoard Board, BoardProcessResult Result)> results,
        IReadOnlyList<Watchlist> candidates,
        RegistryPollingOptions opts,
        CancellationToken ct)
    {
        if (candidates.Count == 0)
            return 0;

        var promoted = 0;

        for (var i = 0; i < results.Count; i++)
        {
            if (promoted >= opts.MaxAutoAddedBoardsPerCycle)
            {
                // A cap that silently swallows work reads as «nothing matched» - say what was left unexamined.
                // The cursor has already moved past these boards, so they come back only after a full walk.
                ctxLog.Warn(
                    "Promotion cap of {Cap} boards per cycle is reached — {Left} boards of this slice are not "
                    + "examined for promotion until the registry walk comes round to them again",
                    opts.MaxAutoAddedBoardsPerCycle, results.Count - i);

                break;
            }

            var (board, result) = results[i];

            if (result.BoardMissing || result.Relevant.Count == 0)
                continue;

            var promotions = await promoter.TryPromoteAsync(board, result.Relevant, candidates, ct);
            if (promotions.Count > 0)
                promoted++;
        }

        return promoted;
    }

    private async Task<BoardProcessResult> ProcessBoardAsync(
        RegisteredBoard board,
        BoardProcessSettings settings,
        RegistryPollingOptions opts,
        CancellationToken ct)
    {
        // No subscriptions: the board is not watched yet, so the run produces state only.
        var work = new BoardWorkItem
        {
            SourceId = board.SourceId,
            BoardId = board.BoardId,
            CompanyName = board.DisplayName ?? board.BoardId,
            Configuration = board.Configuration
        };

        var result = await boardProcessor.ProcessAsync(work, settings, ct);

        progress.UnitFinished(TraversalKind.Registry, board.SourceId, result.BoardMissing || result.Report.Failed);

        if (result.BoardMissing)
        {
            ctxLog.Info("Registry board {Source}/{Board} is gone — deactivated", board.SourceId, board.BoardId);
            await registry.SetActiveAsync(board.SourceId, board.BoardId, false, ct);
        }

        if (opts.DelayBetweenBoardsMs > 0)
            await Task.Delay(opts.DelayBetweenBoardsMs, ct);

        return result;
    }

    /// <summary>
    /// The per-source progress units of the sweep: the active registry as the dataset, the boards the current walk
    /// has already visited as its covered part, and the slice as the plan of this cycle.
    /// </summary>
    private List<TraversalSourceUnits> Coverage(
        IReadOnlyList<RegisteredBoard> boards,
        IReadOnlyList<RegisteredBoard> slice)
    {
        var planned = slice
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
                    DatasetCovered = walkedBySource.GetValueOrDefault(g.Key)
                })
        ];
    }

    private List<RegisteredBoard> TakeSlice(IReadOnlyList<RegisteredBoard> boards, int size)
    {
        if (cursor >= boards.Count)
            cursor = 0;

        // A fresh walk over the registry starts a fresh coverage count.
        if (cursor == 0)
            walkedBySource.Clear();

        var slice = boards.Skip(cursor).Take(size).ToList();

        // The registry is smaller than one slice - wrap around instead of idling.
        if (slice.Count < size && boards.Count > slice.Count)
            slice.AddRange(boards.Take(size - slice.Count));

        cursor = boards.Count == 0 ? 0 : (cursor + slice.Count) % boards.Count;

        return slice;
    }
}
