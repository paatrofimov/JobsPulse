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
    IRejectedPostingStorage rejectedPostings,
    ChangeDetector changeDetector,
    VacancyMatcher matcher,
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

        // Read before the fetch: the source reuses stored vacancies instead of asking for unchanged details again.
        var seen = await stateStore.LoadSeenAsync(board.SourceId, board.BoardId, ct);
        var storageFilters = settings.StorageFilters;

        // Only a source that reads details can skip one, so only then is the rejection memory worth a query.
        var rejected = source.SelectsDetails
            ? await rejectedPostings.LoadAsync(board.SourceId, board.BoardId, ct)
            : new Dictionary<string, RejectedPosting>();

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
                    Configuration = board.Configuration,
                    Known = seen,
                    Rejected = rejected.Values
                        .Where(r => r.FilterHash == settings.StorageFilterHash)
                        .ToDictionary(r => r.PostId, r => r.ListHash, StringComparer.Ordinal),
                    NeedsDescription = storageFilters.Any(f => f.UsesDescription),
                    MayBeStored = v => storageFilters.Any(f => matcher.MayMatchListed(v, f))
                },
                timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            ctxLog.Warn("Board traversal timeout {Board} ({Company})", board.BoardKey, board.CompanyName);
            return BoardProcessResult.Failed();
        }

        if (traverse.BoardMissing)
            return BoardProcessResult.Missing;

        if (!traverse.IsComplete)
        {
            ctxLog.Warn(
                "Incomplete traversal {Board} ({Company}): {Error}. Changes are not applied",
                board.BoardKey, board.CompanyName, traverse.Error);
            return BoardProcessResult.Failed();
        }

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
            // Rows the database would skip anyway are not sent: an unchanged board then commits nothing at all.
            Upserts = [.. detected.VacanciesUpserts.Where(v => IsChanged(v, seen))],
            ClosedPostIds = detected.ClosedPostIds,
            Notifications = notifications,
            FilterHash = settings.StorageFilterHash,
            MatchUpserts = ChangedMatches(detected.MatchUpserts, matches),
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

        if (source.SelectsDetails)
            await SaveRejectedAsync(board, traverse, settings, rejected, ct);

        var report = new BoardReport(
            Fetched: traverse.Vacancies.Count,
            Stored: detected.VacanciesUpserts.Count,
            Matched: detected.MatchUpserts.Count,
            Changes: notifications.Count,
            Failed: false);

        return new BoardProcessResult(report, false, detected.VacanciesUpserts);
    }

    /// <summary>
    /// Remembers the postings whose detail was read (or skipped as known rejected) and that pass no storage filter.
    /// Only the difference to the stored set is written, so an unchanged board costs no round trip. Evaluated here,
    /// not taken from what was not upserted: a duplicate dropped by deduplication was not rejected.
    /// </summary>
    private async Task SaveRejectedAsync(
        BoardWorkItem board,
        SourceTraverseResult traverse,
        BoardProcessSettings settings,
        IReadOnlyDictionary<string, RejectedPosting> stored,
        CancellationToken ct)
    {
        var current = new Dictionary<string, RejectedPosting>(StringComparer.Ordinal);

        foreach (var vacancy in traverse.Vacancies)
        {
            if (vacancy.ListHash is null || vacancy.DescriptionUnavailable)
                continue;

            if (!vacancy.KnownRejected && settings.StorageFilters.Any(f => matcher.Matches(vacancy, f)))
                continue;

            current[vacancy.PostId] = new RejectedPosting(vacancy.PostId, vacancy.ListHash, settings.StorageFilterHash);
        }

        var upserts = current.Values
            .Where(r => !stored.TryGetValue(r.PostId, out var old) || old != r)
            .ToList();

        var removals = stored.Keys
            .Where(postId => !current.ContainsKey(postId))
            .ToList();

        await rejectedPostings.SaveAsync(board.SourceId, board.BoardId, upserts, removals, ct);
    }

    /// <summary>Mirrors the `content_hash IS DISTINCT FROM` guard of the seen_vacancy upsert.</summary>
    private static bool IsChanged(Vacancy vacancy, IReadOnlyDictionary<string, Vacancy> seen)
    {
        return !seen.TryGetValue(vacancy.PostId, out var stored)
               || !string.Equals(stored.ContentHash, VacancyHasher.Compute(vacancy), StringComparison.Ordinal);
    }

    /// <summary>Mirrors the content and filter hash guard of the watchlist_vacancy upsert.</summary>
    private static IReadOnlyList<WatchlistMatch> ChangedMatches(
        IReadOnlyList<WatchlistMatch> detected,
        IReadOnlyList<WatchlistMatch> existing)
    {
        var stored = existing
            .Select(e => (e.WatchlistId, e.PostId, e.ContentHash, e.FilterHash))
            .ToHashSet();

        return [.. detected.Where(m => !stored.Contains((m.WatchlistId, m.PostId, m.ContentHash, m.FilterHash)))];
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
