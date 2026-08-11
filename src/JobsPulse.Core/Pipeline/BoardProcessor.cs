using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Domain.Extensions;
using JobsPulse.Core.Model.Infrastructure;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Core.Pipeline;

/// <summary>
/// Traversal of a single board: fetch once, evaluate every subscribed watchlist, commit state and notifications.
/// Shared by the priority watchlist cycle and the background registry cycle - the only difference between them is
/// which boards they feed here (and whether anything is subscribed at all) and what they do with a dead board.
/// </summary>
public sealed class BoardProcessor(
    ISourceCatalog sourceCatalog,
    IStateStore stateStore,
    ChangeDetector changeDetector,
    ILog log)
{
    private readonly ILog ctxLog = log.ForContext<BoardProcessor>();

    public async Task<BoardProcessResult> ProcessAsync(
        BoardWorkItem board,
        BoardProcessSettings settings,
        CancellationToken ct)
    {
        var source = sourceCatalog.GetSource(board.SourceId);
        if (source is null)
        {
            ctxLog.Warn("Source '{Source}' is not registered — skipping board {Board}", board.SourceId, board.BoardKey);
            return BoardProcessResult.Failed();
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));

        SourceTraverseResult traverse;
        try
        {
            traverse = await source.TraverseTargetAsync(
                new SourceTarget
                {
                    SourceId = board.SourceId,
                    BoardId = board.BoardId,
                    Configuration = board.Configuration
                },
                timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            ctxLog.Warn("Board traversal timeout {Company} ({Board})", board.CompanyName, board.BoardId);
            return BoardProcessResult.Failed();
        }

        if (traverse.BoardMissing)
            return BoardProcessResult.Missing;

        if (!traverse.IsComplete)
        {
            ctxLog.Warn("Incomplete traversal {Company}: {Error}. Changes are not applied", board.CompanyName, traverse.Error);
            return BoardProcessResult.Failed();
        }

        var seen = await stateStore.LoadSeenAsync(board.SourceId, board.BoardId, ct);

        // Nothing is subscribed to a registry board, so its match layer is not even read.
        var matches = board.Subscriptions.Count == 0
            ? []
            : await stateStore.LoadMatchesAsync(board.SourceId, board.BoardId, ct);

        var detected = changeDetector.Detect(new ChangeDetector.Input
        {
            SourceId = board.SourceId,
            BoardId = board.BoardId,
            Traverse = traverse,
            StorageFilters = settings.StorageFilters,
            Subscriptions = board.Subscriptions,
            Seen = seen,
            Matches = matches
        });

        var notifications = settings.DryRun
            ? []
            : BuildNotifications(detected.VacanciesChanges);

        var commitResult = await stateStore.CommitAsync(new StateCommit
        {
            SourceId = board.SourceId,
            BoardId = board.BoardId,
            Upserts = detected.VacanciesUpserts,
            ClosedPostIds = detected.ClosedPostIds,
            Notifications = notifications,
            FilterHash = settings.StorageFilterHash,
            MatchUpserts = detected.MatchUpserts,
            MatchRemovals = detected.MatchRemovals
        }, ct);

        ctxLog.Info(
            "State commit result for board {Board} ({Company}): {Upserts} seen_vacancy upserts, {Closed} seen_vacancy closures, "
            + "{Matches} watchlist_vacancy rows, {Notifications} outbox notifications",
            board.BoardKey, board.CompanyName, commitResult.UpsertVacanciesAffectedRows,
            commitResult.CloseVacanciesAffectedRows, commitResult.MatchAffectedRows, commitResult.OutboxAffectedRows);

        if (settings.DryRun && detected.VacanciesChanges.Count > 0)
        {
            ctxLog.Info(
                "DRY-RUN {Company}: would send {Count} outboxes ({New} new)",
                board.CompanyName, detected.VacanciesChanges.Count,
                detected.VacanciesChanges.Count(c => c.Kind == VacancyChangeKind.New));
        }

        var report = new BoardReport(
            Fetched: traverse.Vacancies.Count,
            Matched: detected.MatchUpserts.Count,
            Changes: notifications.Count,
            Failed: false);

        return new BoardProcessResult(report, false);
    }

    private static IReadOnlyList<OutboxItem> BuildNotifications(IReadOnlyList<VacancyChange> changes)
    {
        return
        [
            // outbox id is db auto-increment -- therefore, can be omitted
            .. changes.Select(c => new OutboxItem
            {
                DedupKey = c.Vacancy.ToDedupKey(c.Kind, c.ContentHash, c.WatchlistId),
                ChangeKind = c.Kind,
                CompanyName = c.CompanyName,
                WatchlistId = c.WatchlistId,
                WatchlistName = c.WatchlistName,
                Vacancy = c.Vacancy,
            })
        ];
    }
}
