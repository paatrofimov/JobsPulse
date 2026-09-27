using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Core.Options;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Core.Pipeline;

/// <summary>
/// Secondary polling cycle over the discovered board registry. The watchlist cycle stays the priority feed -
/// this one walks the registry least-recently-polled first, a slice per cycle, with its own throttling.
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
    IBoardPollStateStorage pollState,
    ITraversalProgressTracker progress,
    IOptionsMonitor<RegistryPollingOptions> options,
    TimeProvider clock,
    ILog log)
{
    private static readonly TimeSpan PaceWindow = TimeSpan.FromDays(1);

    private readonly ILog ctxLog = log.ForContext<RegistryPollingService>();
    private readonly SemaphoreSlim cycleGate = new(1, 1);

    public async Task<CycleRunResult> TryRunCycleAsync(CancellationToken ct)
    {
        if (!await cycleGate.WaitAsync(0, ct))
            return CycleRunResult.Busy;

        try
        {
            var state = await LoadStateAsync(ct);

            return state is null
                ? CycleRunResult.Completed(CycleReport.Empty)
                : CycleRunResult.Completed(await RunCycleCoreAsync(state, DateTimeOffset.MaxValue, ct));
        }
        finally
        {
            cycleGate.Release();
        }
    }

    /// <summary>
    /// Cycle after cycle until <paramref name="until"/> or until every board has been swept once in this call - the
    /// one-shot job, which would otherwise spend seconds of a half-hour budget on one slice. A new cycle is started
    /// only while the longest one so far still fits, so the deadline rarely cuts a slice before it is stamped.
    /// </summary>
    public async Task<CycleRunResult> TryRunSweepAsync(DateTimeOffset until, CancellationToken ct)
    {
        if (!await cycleGate.WaitAsync(0, ct))
            return CycleRunResult.Busy;

        try
        {
            var started = clock.GetUtcNow();
            var cycles = new List<CycleReport>();
            var longest = TimeSpan.Zero;

            // One read of the registry and the poll state for the whole sweep - every slice works on this snapshot.
            var state = await LoadStateAsync(ct);
            if (state is null)
                return CycleRunResult.Completed(CycleReport.Empty);

            while (true)
            {
                var cycleStarted = clock.GetUtcNow();
                var report = await RunCycleCoreAsync(state, started, ct);

                if (report.BoardsProcessed == 0)
                    break;

                cycles.Add(report);

                var now = clock.GetUtcNow();
                longest = TimeSpan.FromTicks(Math.Max(longest.Ticks, (now - cycleStarted).Ticks));

                if (now + longest * 1.5 > until)
                    break;
            }

            var total = CycleReport.Combine(cycles);
            ctxLog.Info(
                "Registry sweep finished: cycles {Cycles}, boards {Boards}, fetched {Fetched}, stored {Stored}, "
                + "errors {Failed} in {Elapsed}",
                cycles.Count, total.BoardsProcessed, total.VacanciesFetched, total.VacanciesStored, total.Failed,
                clock.GetUtcNow() - started);

            return CycleRunResult.Completed(total);
        }
        finally
        {
            cycleGate.Release();
        }
    }

    /// <summary>Null when there is nothing to walk: no enabled watchlist, or no registry board outside them.</summary>
    private async Task<RegistrySweepState?> LoadStateAsync(CancellationToken ct)
    {
        var opts = options.CurrentValue;
        var enabled = await watchlists.GetEnabledAsync(ct);
        var plan = WatchlistPlan.Build(enabled);

        // Without a single enabled watchlist nothing is relevant, so there is nothing to store either.
        if (!plan.HasWatchlists)
        {
            ctxLog.Debug("No enabled watchlists — registry cycle is skipped");
            return null;
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

            return null;
        }

        var polled = new Dictionary<string, DateTimeOffset>(
            await pollState.LoadAsync(ct), StringComparer.OrdinalIgnoreCase);

        return new RegistrySweepState(enabled, plan, boards, polled);
    }

    /// <param name="sweptSince">Boards stamped at or after it are left out of the slice - they are already done in
    /// the running sweep. <see cref="DateTimeOffset.MaxValue"/> for a standalone cycle.</param>
    private async Task<CycleReport> RunCycleCoreAsync(
        RegistrySweepState state,
        DateTimeOffset sweptSince,
        CancellationToken ct)
    {
        var opts = options.CurrentValue;
        var (enabled, plan, boards, polled) = state;

        var now = clock.GetUtcNow();

        var slice = TakeSlice(boards, polled, sweptSince, opts.BoardsPerCycle);
        if (slice.Count == 0)
        {
            ctxLog.Info("Every registry board has been swept in this run");
            return CycleReport.Empty;
        }

        progress.CycleStarted(TraversalKind.Registry, Coverage(boards, slice, polled, now));

        var walk = WalkLength(boards, polled, now);
        ctxLog.Info(
            "Start registry cycle: {Slice} boards, {Covered} of {Total} registry boards swept within one walk "
            + "(a walk takes {Walk} at the current pace)",
            slice.Count, Swept(boards, polled, now - walk), boards.Count, FormatWalk(walk));

        var settings = new BoardProcessSettings(
            opts.SingleEntryProcessTimeoutSeconds,
            opts.DryRun,
            plan.StorageFilters,
            plan.StorageFilterHash,
            plan.DescriptionRulesHash);

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

        // Every board of the slice is stamped, failures included: an unstamped board would stay the
        // least-recently-polled one forever and the walk would never get past it.
        await pollState.StampAsync([.. slice.Select(b => new BoardPollStamp(b.SourceId, b.BoardId, now))], ct);

        foreach (var board in slice)
            polled[$"{board.SourceId}/{board.BoardId}"] = now;

        // Promotion is database work only, so it runs after the fetch pass: one writer, and the cap is exact.
        var promoted = await PromoteAsync(results, SelectPromotionCandidates(enabled, opts), opts, ct);

        // Only after the promotion: it enqueues notifications too, and an idle traversal lets the open window leave.
        progress.CycleFinished(TraversalKind.Registry, Coverage(boards, [], polled, now));

        var report = CycleReport.Aggregate([.. results.Select(r => r.Result.Report)]);
        ctxLog.Info(
            "Registry cycle finished: boards {Boards}, fetched {Fetched}, stored {Stored}, errors {Failed}, "
            + "promotions {Promoted}",
            report.BoardsProcessed, report.VacanciesFetched, report.VacanciesStored, report.Failed, promoted);

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
                // These boards are already stamped as polled, so they come back only after a full walk.
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
    /// The per-source progress units of the sweep: the active registry as the dataset, the boards swept within one
    /// walk as its covered part, and the slice as the plan of this cycle.
    /// </summary>
    private static List<TraversalSourceUnits> Coverage(
        IReadOnlyList<RegisteredBoard> boards,
        IReadOnlyList<RegisteredBoard> slice,
        IReadOnlyDictionary<string, DateTimeOffset> polled,
        DateTimeOffset now)
    {
        var since = now - WalkLength(boards, polled, now);

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
                    DatasetCovered = g.Count(b => IsSwept(b, polled, since))
                })
        ];
    }

    private static int Swept(
        IReadOnlyList<RegisteredBoard> boards,
        IReadOnlyDictionary<string, DateTimeOffset> polled,
        DateTimeOffset since) =>
        boards.Count(b => IsSwept(b, polled, since));

    private static bool IsSwept(
        RegisteredBoard board,
        IReadOnlyDictionary<string, DateTimeOffset> polled,
        DateTimeOffset since) =>
        polled.TryGetValue($"{board.SourceId}/{board.BoardId}", out var at) && at >= since;

    /// <summary>
    /// How long one full walk over the registry takes at the pace the sweep really keeps: the boards swept over the
    /// last <see cref="PaceWindow"/> extrapolated to the whole registry. Measured rather than derived from
    /// `CycleIntervalMinutes`, because a one-shot job runs on a cron schedule that option knows nothing about.
    /// Zero when nothing was swept lately - the walk is not moving.
    /// </summary>
    private static TimeSpan WalkLength(
        IReadOnlyList<RegisteredBoard> boards,
        IReadOnlyDictionary<string, DateTimeOffset> polled,
        DateTimeOffset now)
    {
        var recent = Swept(boards, polled, now - PaceWindow);
        if (recent == 0)
            return TimeSpan.Zero;

        return PaceWindow * Math.Max(1d, boards.Count / (double)recent);
    }

    private static string FormatWalk(TimeSpan walk) =>
        walk == TimeSpan.Zero
            ? "unknown"
            : walk.TotalDays >= 1 ? $"{walk.TotalDays:F1} days" : $"{walk.TotalHours:F1} hours";

    /// <summary>
    /// The slice of this cycle: the least recently polled boards first. The order is taken from
    /// `board_poll_state`, so a restart continues the walk where it stopped instead of starting it over, and a
    /// board that has never been polled (a fresh discovery) is picked up before anything else.
    /// </summary>
    private static List<RegisteredBoard> TakeSlice(
        IReadOnlyList<RegisteredBoard> boards,
        IReadOnlyDictionary<string, DateTimeOffset> polled,
        DateTimeOffset sweptSince,
        int size) =>
    [
        .. boards
            .Select(b => (Board: b, At: polled.TryGetValue($"{b.SourceId}/{b.BoardId}", out var at)
                ? at
                : DateTimeOffset.MinValue))
            .Where(x => x.At < sweptSince)
            .OrderBy(x => x.At)
            .ThenBy(x => x.Board.SourceId, StringComparer.Ordinal)
            .ThenBy(x => x.Board.BoardId, StringComparer.Ordinal)
            .Take(Math.Max(1, size))
            .Select(x => x.Board)
    ];
}
