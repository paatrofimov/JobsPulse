using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Discovery.Abstractions;
using JobsPulse.Discovery.Infrastructure;
using JobsPulse.Discovery.Models;
using JobsPulse.Discovery.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Discovery.Pipeline;

/// <summary>
/// Discovery over the cdx http api: one source at a time, one collection at a time, one index page at a time.
/// Slow and heavily throttled, which is why it is opt-in - see <see cref="ParquetIndexDiscoveryPass"/> for the
/// default reader.
/// </summary>
public sealed class HttpIndexDiscoveryPass(
    ICrawlIndexClient index,
    IEnumerable<IBoardUrlParser> parsers,
    IBoardRegistryStorage registry,
    BoardTokenSink sink,
    TimeProvider clock,
    ILog log)
{
    private readonly ILog ctxLog = log.ForContext<HttpIndexDiscoveryPass>();
    private readonly IReadOnlyList<IBoardUrlParser> boardUrlParsers = parsers.ToList();

    public async Task<BoardDiscoveryReport> RunAsync(
        IReadOnlyList<CrawlCollection> collections,
        bool full,
        DiscoveryOptions opts,
        CancellationToken ct)
    {
        using var stage = StageTimer.Start(ctxLog, "http index pass");

        var report = BoardDiscoveryReport.Empty;

        foreach (var parser in boardUrlParsers)
        {
            ct.ThrowIfCancellationRequested();
            report = DiscoveryReports.Merge(report, await DiscoverSourceAsync(parser, collections, full, opts, ct));
        }

        return report;
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

                totals = DiscoveryReports.Merge(totals, DiscoveryReports.Pending(pending.Count - position + 1));
                break;
            }

            using var stage = StageTimer.Start(
                ctxLog,
                $"http scan of {collection.Id} for {parser.SourceId} ({position}/{pending.Count})");

            var scan = await ScanCollectionAsync(parser, collection, known, budget, opts, ct);

            // Tokens found before the failure are still worth keeping - the upsert is idempotent.
            var added = await sink.ValidateAndStoreAsync(
                parser.SourceId, scan.Tokens, $"common-crawl:{collection.Id}", opts, ct);

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
                stage.Outcome("gave up");

                ctxLog.Warn(
                    "Collection {Collection} stays pending ({Status}): {Records} records, {Tokens} tokens, "
                    + "{Added} new boards. The next run will walk it again",
                    collection.Id, scan.Status, scan.Records, scan.Tokens.Count, added);
            }

            totals = DiscoveryReports.Merge(totals, new BoardDiscoveryReport(
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

                    totals = DiscoveryReports.Merge(totals, DiscoveryReports.Pending(pending.Count - position));
                    break;
                }
            }

            await DiscoveryPause.WaitAsync(ctxLog, opts.PauseBetweenCollectionsMsec, "collections", ct);
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

                await DiscoveryPause.WaitAsync(ctxLog, opts.PauseBetweenPagesMsec, "pages", ct);
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
}
