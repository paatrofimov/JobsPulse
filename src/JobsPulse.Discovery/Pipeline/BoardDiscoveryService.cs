using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Discovery.Abstractions;
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
            return new BoardDiscoveryReport(true, 0, 0, 0, 0, 0);
        }

        var window = SelectCollections(collections, full, opts);

        var report = new BoardDiscoveryReport(true, 0, 0, 0, 0, 0);

        foreach (var parser in boardUrlParsers)
        {
            ct.ThrowIfCancellationRequested();
            report = Merge(report, await DiscoverSourceAsync(parser, window, full, opts, ct));
        }

        ctxLog.Info(
            "Discovery finished: {Collections} indexes, {Records} records, {Tokens} tokens, {Added} new boards",
            report.CollectionsProcessed, report.RecordsSeen, report.TokensFound, report.BoardsAdded);

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
            return new BoardDiscoveryReport(true, 0, 0, 0, 0, 0);
        }

        ctxLog.Debug("Pending collections to crawl: {Count}. Known: {Known}, Processed: {Processed}", pending.Count, known.Count, processed.Count);

        var totals = new BoardDiscoveryReport(true, 0, 0, 0, 0, 0);

        foreach (var collection in pending)
        {
            ct.ThrowIfCancellationRequested();

            var (records, tokens) = await ScanCollectionAsync(parser, collection, known, opts, ct);

            // Validation is the expensive part - tokens are checked against the ATS itself before being stored.
            var added = await ValidateAndStoreAsync(parser.SourceId, collection, tokens, opts, ct);

            foreach (var token in tokens)
                known.Add(token);

            await registry.MarkCrawlProcessedAsync(new CrawlIndexProgress
            {
                SourceId = parser.SourceId,
                CollectionId = collection.Id,
                RecordsSeen = records,
                TokensFound = tokens.Count,
                BoardsAdded = added,
                ProcessedAt = clock.GetUtcNow()
            }, ct);

            totals = Merge(totals, new BoardDiscoveryReport(true, 1, records, tokens.Count, tokens.Count, added));
        }

        return totals;
    }

    private async Task<(long Records, List<string> Tokens)> ScanCollectionAsync(
        IBoardUrlParser parser,
        CrawlCollection collection,
        HashSet<string> known,
        DiscoveryOptions opts,
        CancellationToken ct)
    {
        var fresh = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long records = 0;

        foreach (var pattern in parser.IndexUrlPatterns)
        {
            var query = new CrawlIndexQuery
            {
                Collection = collection,
                UrlPattern = pattern,
                PageSize = opts.PageSize
            };

            var pages = await index.GetPageCountAsync(query, ct);
            if (pages == 0)
            {
                ctxLog.Debug("No pages for collection {Collection} and pattern {Pattern}", collection.Id, pattern);
                continue;
            }

            if (opts.MaxPagesPerCollection > 0)
                pages = Math.Min(pages, opts.MaxPagesPerCollection);

            ctxLog.Info(
                "Scanning {Collection} for '{Pattern}': {Pages} index pages",
                collection.Id, pattern, pages);

            for (var page = 0; page < pages; page++)
            {
                await foreach (var record in index.StreamPageAsync(query, page, ct))
                {
                    records++;

                    if (!parser.TryParseBoardId(record.Url, out var boardId))
                        continue;

                    if (known.Contains(boardId))
                        continue;

                    fresh.Add(boardId);

                    if (fresh.Count >= opts.MaxNewTokensPerRun)
                    {
                        ctxLog.Warn(
                            "Token cap {Cap} reached on {Collection} — the rest is left for the next run",
                            opts.MaxNewTokensPerRun, collection.Id);

                        return (records, fresh.ToList());
                    }
                }

                if (options.CurrentValue.PauseBetweenPagesMsec.HasValue)
                {
                    var pause = TimeSpan.FromMilliseconds(options.CurrentValue.PauseBetweenPagesMsec.Value);
                    await Task.Delay(pause, ct);
                }
            }
        }

        return (records, fresh.ToList());
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

    private static BoardDiscoveryReport Merge(BoardDiscoveryReport a, BoardDiscoveryReport b) => new(
        true,
        a.CollectionsProcessed + b.CollectionsProcessed,
        a.RecordsSeen + b.RecordsSeen,
        a.TokensFound + b.TokensFound,
        a.Validated + b.Validated,
        a.BoardsAdded + b.BoardsAdded);
}