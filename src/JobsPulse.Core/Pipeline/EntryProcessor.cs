using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Domain.Extensions;
using JobsPulse.Core.Model.Infrastructure;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Core.Pipeline;

/// <summary>
/// Traversal of a single board: fetch, filter, detect changes, commit state and notifications.
/// Shared by the priority watchlist cycle and the background registry cycle - the only difference between them is
/// which entries they feed here and what they do with a dead board.
/// </summary>
public sealed class EntryProcessor(
    ISourceCatalog sourceCatalog,
    IStateStore stateStore,
    VacancyMatcher vacancyMatcher,
    ChangeDetector changeDetector,
    ILog log)
{
    private readonly ILog ctxLog = log.ForContext<EntryProcessor>();

    public async Task<EntryProcessResult> ProcessAsync(
        WatchEntry entry,
        FilterSpec? filter,
        EntryProcessSettings settings,
        CancellationToken ct)
    {
        var source = sourceCatalog.GetSource(entry.VacancySourceId);
        if (source is null)
        {
            ctxLog.Warn("Source '{Source}' is not registered — skipping entry {Entry}", entry.VacancySourceId, entry.Id);
            return EntryProcessResult.Failed();
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));

        SourceTraverseResult traverse;
        try
        {
            traverse = await source.TraverseTargetAsync(
                new SourceTarget { SourceId = entry.VacancySourceId, BoardId = entry.BoardId },
                timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            ctxLog.Warn("Board traversal timeout {Company} ({Board})", entry.CompanyName, entry.BoardId);
            return EntryProcessResult.Failed();
        }

        if (traverse.BoardMissing)
            return EntryProcessResult.Missing;

        if (!traverse.IsComplete)
        {
            ctxLog.Warn("Incomplete traversal {Company}: {Error}. Changes are not applied", entry.CompanyName, traverse.Error);
            return EntryProcessResult.Failed();
        }

        var matched = vacancyMatcher.Apply(traverse.Vacancies, filter);
        var seen = await stateStore.LoadSeenAsync(entry.VacancySourceId, entry.BoardId, ct);

        var detected = changeDetector.Detect(new ChangeDetector.Input
        {
            Entry = entry,
            Traverse = traverse,
            Matched = matched,
            Seen = seen
        });

        var notifications = settings.DryRun
            ? []
            : BuildNotifications(detected.VacanciesChanges);

        var commitResult = await stateStore.CommitAsync(new StateCommit
        {
            SourceId = entry.VacancySourceId,
            BoardId = entry.BoardId,
            Upserts = detected.VacanciesUpserts,
            ClosedPostIds = detected.ClosedPostIds,
            Notifications = notifications,
            FilterHash = filter is null ? null : VacancyHasher.ComputeFilterHash(filter)
        }, ct);

        ctxLog.Info(
            "State commit result for company {Company}: {Upserts} seen_vacancy upserts, {Closed} seen_vacancy closures, {Notifications} outbox notification",
            entry.CompanyName, commitResult.UpsertVacanciesAffectedRows, commitResult.CloseVacanciesAffectedRows, commitResult.OutboxAffectedRows);

        if (settings.DryRun && detected.VacanciesChanges.Count > 0)
        {
            ctxLog.Info(
                "DRY-RUN {Company}: would send {Count} outboxes ({New} new)",
                entry.CompanyName, detected.VacanciesChanges.Count,
                detected.VacanciesChanges.Count(c => c.Kind == VacancyChangeKind.New));
        }

        var report = new EntryReport(
            Fetched: traverse.Vacancies.Count,
            Matched: matched.Count,
            Changes: notifications.Count,
            Failed: false);

        return new EntryProcessResult(report, false);
    }

    private static IReadOnlyList<OutboxItem> BuildNotifications(IReadOnlyList<VacancyChange> changes)
    {
        return
        [
            // outbox id is db auto-increment -- therefore, can be omitted
            .. changes.Select(c => new OutboxItem
            {
                DedupKey = c.Vacancy.ToDedupKey(c.Kind, c.ContentHash),
                ChangeKind = c.Kind,
                CompanyName = c.CompanyName,
                Vacancy = c.Vacancy,
            })
        ];
    }
}
