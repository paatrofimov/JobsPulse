using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Domain.Extensions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Core.Options;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Core.Pipeline;

public sealed class PollingOrchestrator(
    IWatchlistProvider watchlistProvider,
    ISourceCatalog sourceCatalog,
    IStateStore stateStore,
    VacancyMatcher vacancyMatcher,
    ChangeDetector changeDetector,
    IOptionsMonitor<WatchlistPollingOptions> options,
    TimeProvider clock,
    ILog log)
{
    private readonly ILog ctxLog = log.ForContext<PollingOrchestrator>();
    private readonly Dictionary<string, DateTimeOffset> _lastRunByEntry = new(StringComparer.OrdinalIgnoreCase);

    public async Task<CycleReport> RunCycleAsync(CancellationToken ct)
    {
        var opts = options.CurrentValue;
        var current = watchlistProvider.Current;
        var now = clock.GetUtcNow();

        var due = current.Entries.Where(e => e.Enabled && IsDue(e, opts, now)).ToList();

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
        var source = sourceCatalog.GetSource(entry.VacancySourceId);
        if (source is null)
        {
            ctxLog.Warn("Source '{Source}' is not registered — skipping entry {Entry}", entry.VacancySourceId, entry.Id);
            return EntryReport.Failure();
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(opts.SingleEntryProcessTimeoutSeconds));

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
            return EntryReport.Failure();
        }

        if (traverse.BoardMissing)
        {
            ctxLog.Warn("Board {Board} ({Company}) not found — disabling watchlist entry {Id}", entry.BoardId, entry.CompanyName, entry.Id);
            await watchlistProvider.SetEnabledAsync(entry.Id, false, ct);
            return EntryReport.Failure();
        }

        if (!traverse.IsComplete)
        {
            ctxLog.Warn("Incomplete traversal {Company}: {Error}. Changes are not applied", entry.CompanyName, traverse.Error);
            return EntryReport.Failure();
        }

        var filter = entry.CustomFilter ?? config.DefaultFilter;
        var matched = vacancyMatcher.Apply(traverse.Vacancies, filter);
        var seen = await stateStore.LoadSeenAsync(entry.VacancySourceId, entry.BoardId, ct);

        var detected = changeDetector.Detect(new ChangeDetector.Input
        {
            Entry = entry,
            Traverse = traverse,
            Matched = matched,
            Seen = seen
        });

        var notifications = opts.DryRun
            ? []
            : BuildNotifications(detected.VacanciesChanges);

        var commitResult = await stateStore.CommitAsync(new StateCommit
        {
            SourceId = entry.VacancySourceId,
            BoardId = entry.BoardId,
            Upserts = detected.VacanciesUpserts,
            ClosedPostIds = detected.ClosedPostIds,
            Notifications = notifications
        }, ct);

        ctxLog.Info(
            "State commit result for company {Company}: {Upserts} seen_vacancy upserts, {Closed} seen_vacancy closures, {Notifications} outbox notification",
            entry.CompanyName, commitResult.UpsertVacanciesAffectedRows, commitResult.CloseVacanciesAffectedRows, commitResult.OutboxAffectedRows);

        if (opts.DryRun && detected.VacanciesChanges.Count > 0)
        {
            ctxLog.Info(
                "DRY-RUN {Company}: would send {Count} outboxes ({New} new)",
                entry.CompanyName, detected.VacanciesChanges.Count,
                detected.VacanciesChanges.Count(c => c.Kind == VacancyChangeKind.New));
        }

        return new EntryReport(
            Fetched: traverse.Vacancies.Count,
            Matched: matched.Count,
            Changes: detected.VacanciesChanges.Count,
            Failed: false);
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