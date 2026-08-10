using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Discovery.Abstractions;
using JobsPulse.Discovery.Infrastructure;
using JobsPulse.Discovery.Models;
using JobsPulse.Discovery.Options;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Discovery.Pipeline;

/// <summary>
/// Mines crawl indexes for ATS board urls and keeps the accumulative board registry up to date.
/// Index reading is generic; everything ATS-specific comes from <see cref="IBoardUrlParser"/> implementations.
/// </summary>
public sealed class BoardDiscoveryService(
    ICrawlIndexClient index,
    IEnumerable<IBoardUrlParser> parsers,
    ISourceCatalog sources,
    IBoardRegistryStorage registry,
    IOptionsMonitor<DiscoveryOptions> options,
    TimeProvider clock,
    ILog log) : IBoardDiscoveryService
{
    private readonly ILog ctxLog = log.ForContext<BoardDiscoveryService>();
    private readonly SemaphoreSlim runGate = new(1, 1);
    private readonly IReadOnlyList<IBoardUrlParser> boardUrlParsers = parsers.ToList();

    public async Task<BoardDiscoveryReport> RunAsync(bool full, CancellationToken ct)
    {
        // Discovery is heavy and stateful - a second run in parallel would only re-read the same pages.
        if (!await runGate.WaitAsync(0, ct))
            return BoardDiscoveryReport.Busy;

        try
        {
            return await RunCoreAsync(full, ct);
        }
        finally
        {
            runGate.Release();
        }
    }

    private async Task<BoardDiscoveryReport> RunCoreAsync(bool full, CancellationToken ct)
    {
        var opts = options.CurrentValue;

        var collections = await index.GetCollectionsAsync(ct);
        if (collections.Count == 0)
        {
            ctxLog.Warn("Crawl index has returned no collections — nothing to discover");
            return BoardDiscoveryReport.Empty;
        }

        var window = SelectCollections(collections, full, opts);

        var report = BoardDiscoveryReport.Empty;

        foreach (var parser in boardUrlParsers)
        {
            ct.ThrowIfCancellationRequested();
            report = Merge(report, await DiscoverSourceAsync(parser, window, full, opts, ct));
        }

        ctxLog.Info(
            "Discovery finished: {Collections} indexes scanned, {Failed} failed, {Pending} left pending, "
            + "{Records} records, {Tokens} tokens, {Added} new boards",
            report.CollectionsProcessed, report.CollectionsFailed, report.CollectionsPending,
            report.RecordsSeen, report.TokensFound, report.BoardsAdded);

        return report;
    }

    /// <summary>Bootstrap takes the union of the last N years; an incremental run takes only fresh indexes.</summary>
    private IReadOnlyList<CrawlCollection> SelectCollections(
        IReadOnlyList<CrawlCollection> collections,
        bool full,
        DiscoveryOptions opts)
    {
        if (!full)
            return collections;

        var since = clock.GetUtcNow().Year - Math.Max(1, opts.BootstrapYears) + 1;

        var crawlCollections = collections
            .Where(c => c.Year == 0 || c.Year >= since)
            .OrderBy(c => c.Id, StringComparer.Ordinal)
            .ToList();

        return crawlCollections;
    }

    private async Task<BoardDiscoveryReport> DiscoverSourceAsync(
        IBoardUrlParser parser,
        IReadOnlyList<CrawlCollection> collections,
        bool full,
        DiscoveryOptions opts,
        CancellationToken ct)
    {
        var processed = full
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : (await registry.GetProcessedCrawlsAsync(parser.SourceId, ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var known = (await registry.GetKnownBoardIdsAsync(parser.SourceId, ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var pending = collections.Where(c => !processed.Contains(c.Id)).ToList();
        if (pending.Count == 0)
        {
            ctxLog.Debug("No new crawl indexes for source {Source}", parser.SourceId);
            return BoardDiscoveryReport.Empty;
        }

        ctxLog.Debug("Pending collections to crawl: {Count}. Known: {Known}, Processed: {Processed}", pending.Count, known.Count, processed.Count);

        var totals = BoardDiscoveryReport.Empty;
        var newTokens = 0;
        var failuresInARow = 0;
        var position = 0;

        foreach (var collection in pending)
        {
            ct.ThrowIfCancellationRequested();
            position++;

            var budget = Math.Max(0, opts.MaxNewTokensPerRun - newTokens);
            if (budget == 0)
            {
                ctxLog.Warn(
                    "Token cap {Cap} is exhausted for {Source} — {Left} collections are left for the next run",
                    opts.MaxNewTokensPerRun, parser.SourceId, pending.Count - position + 1);

                totals = Merge(totals, Pending(pending.Count - position + 1));
                break;
            }

            ctxLog.Info(
                "Scanning collection {Collection} ({Index}/{Total}) for {Source}",
                collection.Id, position, pending.Count, parser.SourceId);

            var scan = await ScanCollectionAsync(parser, collection, known, budget, opts, ct);

            // Tokens found before the failure are still worth keeping - the upsert is idempotent.
            var added = await ValidateAndStoreAsync(parser.SourceId, collection, scan.Tokens, opts, ct);

            foreach (var token in scan.Tokens)
                known.Add(token);

            newTokens += scan.Tokens.Count;

            if (scan.Completed)
            {
                await registry.MarkCrawlProcessedAsync(new CrawlIndexProgress
                {
                    SourceId = parser.SourceId,
                    CollectionId = collection.Id,
                    RecordsSeen = scan.Records,
                    TokensFound = scan.Tokens.Count,
                    BoardsAdded = added,
                    ProcessedAt = clock.GetUtcNow()
                }, ct);

                failuresInARow = 0;

                ctxLog.Info(
                    "Collection {Collection} is done and marked processed: {Records} records, {Tokens} tokens, {Added} new boards",
                    collection.Id, scan.Records, scan.Tokens.Count, added);
            }
            else
            {
                ctxLog.Warn(
                    "Collection {Collection} stays pending ({Status}): {Records} records, {Tokens} tokens, "
                    + "{Added} new boards. The next run will walk it again",
                    collection.Id, scan.Status, scan.Records, scan.Tokens.Count, added);
            }

            totals = Merge(totals, new BoardDiscoveryReport(
                true,
                scan.Completed ? 1 : 0,
                scan.Records,
                scan.Tokens.Count,
                scan.Tokens.Count,
                added,
                scan.Failed ? 1 : 0,
                scan.Completed ? 0 : 1));

            if (scan.Failed)
            {
                failuresInARow++;

                var maxFailures = Math.Max(1, opts.MaxConsecutiveCollectionFailures);
                if (failuresInARow >= maxFailures)
                {
                    ctxLog.Error(
                        "{Failures} crawl collections in a row have failed for {Source} — the index looks unavailable, "
                        + "giving up on this run. {Left} collections are left pending",
                        failuresInARow, parser.SourceId, pending.Count - position);

                    totals = Merge(totals, Pending(pending.Count - position));
                    break;
                }
            }

            await PauseAsync(opts.PauseBetweenCollectionsMsec, "collections", ct);
        }

        return totals;
    }

    private async Task<CollectionScanResult> ScanCollectionAsync(
        IBoardUrlParser parser,
        CrawlCollection collection,
        HashSet<string> known,
        int budget,
        DiscoveryOptions opts,
        CancellationToken ct)
    {
        var fresh = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long records = 0;
        var failed = false;
        var capReached = false;

        foreach (var pattern in parser.IndexUrlPatterns)
        {
            if (capReached)
                break;

            var query = new CrawlIndexQuery
            {
                Collection = collection,
                UrlPattern = pattern,
                PageSize = opts.PageSize
            };

            int pages;

            try
            {
                pages = await index.GetPageCountAsync(query, ct);
            }
            catch (Exception ex) when (CrawlIndexFailure.IsTransient(ex))
            {
                // Without the page count there is nothing to iterate - the pattern is retried by the next run.
                ctxLog.Warn(
                    ex,
                    "Page count of {Collection} for '{Pattern}' is unavailable ({Reason}) — skipping the pattern",
                    collection.Id, pattern, CrawlIndexFailure.Describe(ex));

                failed = true;
                continue;
            }

            if (pages == 0)
            {
                ctxLog.Debug("No pages for collection {Collection} and pattern {Pattern}", collection.Id, pattern);
                continue;
            }

            var total = pages;
            if (opts.MaxPagesPerCollection > 0 && total > opts.MaxPagesPerCollection)
            {
                total = opts.MaxPagesPerCollection;

                ctxLog.Warn(
                    "Collection {Collection} for '{Pattern}' has {Pages} pages, only {Cap} are read (MaxPagesPerCollection)",
                    collection.Id, pattern, pages, total);
            }

            ctxLog.Info("Scanning {Collection} for '{Pattern}': {Pages} index pages", collection.Id, pattern, total);

            var pageFailures = 0;

            for (var page = 0; page < total; page++)
            {
                ct.ThrowIfCancellationRequested();

                var result = await ScanPageAsync(parser, query, page, total, known, fresh, budget, ct);
                records += result.Records;

                if (result.Failed)
                {
                    failed = true;
                    pageFailures++;

                    var maxPageFailures = Math.Max(1, opts.MaxPageFailuresPerCollection);
                    if (pageFailures >= maxPageFailures)
                    {
                        ctxLog.Warn(
                            "{Failures} pages of {Collection} for '{Pattern}' have failed — abandoning the collection, "
                            + "it stays pending",
                            pageFailures, collection.Id, pattern);

                        return new CollectionScanResult
                        {
                            Records = records,
                            Tokens = fresh.ToList(),
                            Failed = true
                        };
                    }
                }

                if (result.CapReached)
                {
                    ctxLog.Warn(
                        "Token cap {Cap} reached on {Collection} — the rest is left for the next run",
                        budget, collection.Id);

                    capReached = true;
                    break;
                }

                await PauseAsync(opts.PauseBetweenPagesMsec, "pages", ct);
            }
        }

        return new CollectionScanResult
        {
            Records = records,
            Tokens = fresh.ToList(),
            Failed = failed,
            CapReached = capReached
        };
    }

    /// <summary>One index page. A failure here is local: the caller decides whether to move on or to give up.</summary>
    private async Task<PageScanResult> ScanPageAsync(
        IBoardUrlParser parser,
        CrawlIndexQuery query,
        int page,
        int pages,
        HashSet<string> known,
        HashSet<string> fresh,
        int budget,
        CancellationToken ct)
    {
        long records = 0;

        try
        {
            await foreach (var record in index.StreamPageAsync(query, page, ct))
            {
                records++;

                if (!parser.TryParseBoardId(record.Url, out var boardId))
                    continue;

                if (known.Contains(boardId))
                    continue;

                fresh.Add(boardId);

                if (fresh.Count >= budget)
                {
                    return new PageScanResult
                    {
                        Records = records,
                        CapReached = true
                    };
                }
            }

            ctxLog.Debug(
                "Page {Page}/{Pages} of {Collection} for '{Pattern}' is read: {Records} records, {Tokens} unknown tokens so far",
                page + 1, pages, query.Collection.Id, query.UrlPattern, records, fresh.Count);

            return new PageScanResult { Records = records };
        }
        catch (Exception ex) when (CrawlIndexFailure.IsTransient(ex))
        {
            ctxLog.Warn(
                ex,
                "Page {Page}/{Pages} of {Collection} for '{Pattern}' has failed ({Reason}) — moving to the next page",
                page + 1, pages, query.Collection.Id, query.UrlPattern, CrawlIndexFailure.Describe(ex));

            return new PageScanResult
            {
                Records = records,
                Failed = true
            };
        }
    }

    private async Task<int> ValidateAndStoreAsync(
        string sourceId,
        CrawlCollection collection,
        IReadOnlyList<string> tokens,
        DiscoveryOptions opts,
        CancellationToken ct)
    {
        if (tokens.Count == 0)
            return 0;

        var resolver = sources.GetResolver(sourceId);
        if (resolver is null)
        {
            ctxLog.Warn("Source '{Source}' has no resolver — tokens cannot be validated", sourceId);
            return 0;
        }

        var now = clock.GetUtcNow();
        var discoveredVia = $"common-crawl:{collection.Id}";

        using var gate = new SemaphoreSlim(Math.Max(1, opts.ValidationConcurrency));

        var added = 0;
        var batch = new List<RegisteredBoard>(opts.UpsertBatchSize);

        foreach (var chunk in tokens.Chunk(Math.Max(1, opts.UpsertBatchSize)))
        {
            var probes = await Task.WhenAll(chunk.Select(async token =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    return await ProbeSafeAsync(resolver, token, ct);
                }
                finally
                {
                    gate.Release();
                }
            }));

            batch.Clear();
            batch.AddRange(probes
                .Where(c => c is not null)
                .Select(c => new RegisteredBoard
                {
                    SourceId = c!.SourceId,
                    BoardId = c.BoardId,
                    DisplayName = c.DisplayName,
                    JobCount = c.JobCount,
                    BoardUrl = c.BoardUrl,
                    DiscoveredVia = discoveredVia,
                    DiscoveredAt = now,
                    LastValidatedAt = now,
                    IsActive = true
                }));

            if (batch.Count > 0)
                added += await registry.UpsertAsync(batch, ct);
        }

        ctxLog.Debug("Validation of {Collection}: {Tokens} tokens, {Added} new boards", collection.Id, tokens.Count, added);

        return added;
    }

    private async Task<BoardCandidate?> ProbeSafeAsync(IBoardResolver resolver, string token, CancellationToken ct)
    {
        try
        {
            return await resolver.ProbeAsync(token, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ctxLog.Debug(ex, "Validation of board token '{Token}' has failed", token);
            return null;
        }
    }

    private async Task PauseAsync(long? milliseconds, string between, CancellationToken ct)
    {
        if (milliseconds is null or <= 0)
            return;

        var pause = TimeSpan.FromMilliseconds(milliseconds.Value);

        ctxLog.Debug("Pausing {Pause} between {Between}", pause, between);

        await Task.Delay(pause, ct);
    }

    private static BoardDiscoveryReport Pending(int collections) =>
        new(true, 0, 0, 0, 0, 0, 0, Math.Max(0, collections));

    private static BoardDiscoveryReport Merge(BoardDiscoveryReport a, BoardDiscoveryReport b) => new(
        true,
        a.CollectionsProcessed + b.CollectionsProcessed,
        a.RecordsSeen + b.RecordsSeen,
        a.TokensFound + b.TokensFound,
        a.Validated + b.Validated,
        a.BoardsAdded + b.BoardsAdded,
        a.CollectionsFailed + b.CollectionsFailed,
        a.CollectionsPending + b.CollectionsPending);
}
