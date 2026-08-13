using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Discovery.Abstractions;
using JobsPulse.Discovery.Infrastructure;
using JobsPulse.Discovery.Models;
using JobsPulse.Discovery.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Discovery.Pipeline;

/// <summary>
/// Discovery over the columnar index. Unlike the http pass this one is collection-major: a query costs the same
/// whether it asks about one ATS or ten, so every registered <see cref="IBoardUrlParser"/> is folded into a single
/// pass over the parquet files, and adding an ATS costs nothing extra.
///
/// A collection is read in three stages, each narrowing the file set before a wider column is paid for - see
/// <see cref="ParquetIndexSql"/> for why that is the difference between minutes and hours.
/// </summary>
public sealed class ParquetIndexDiscoveryPass(
    ICrawlIndexFileCatalog files,
    IParquetIndexClient parquet,
    IEnumerable<IBoardUrlParser> parsers,
    IBoardRegistryStorage registry,
    BoardTokenSink sink,
    TimeProvider clock,
    ILog log)
{
    private readonly ILog ctxLog = log.ForContext<ParquetIndexDiscoveryPass>();
    private readonly IReadOnlyList<IBoardUrlParser> boardUrlParsers = parsers.ToList();

    public async Task<BoardDiscoveryReport> RunAsync(
        IReadOnlyList<CrawlCollection> collections,
        bool full,
        DiscoveryOptions opts,
        CancellationToken ct)
    {
        var targets = BoardIndexTargets.From(boardUrlParsers);
        if (targets.Count == 0)
        {
            ctxLog.Warn("No board url patterns are registered — the columnar index has nothing to be asked about");
            return BoardDiscoveryReport.Empty;
        }

        using var pass = StageTimer.Start(
            ctxLog,
            $"parquet index pass over {collections.Count} collection(s) for {boardUrlParsers.Count} source(s)");

        ctxLog.Info(
            "Columnar index targets: {Targets}",
            string.Join(", ", targets.Select(t => $"{t.SourceId}:{t.HostLabel}{t.PathPrefix}")));

        var state = await LoadStateAsync(full, ct);

        var totals = BoardDiscoveryReport.Empty;
        var failuresInARow = 0;
        var position = 0;

        foreach (var collection in collections)
        {
            ct.ThrowIfCancellationRequested();
            position++;

            var pendingSources = state.Values
                .Where(s => !s.Processed.Contains(collection.Id))
                .Select(s => s.SourceId)
                .ToList();

            if (pendingSources.Count == 0)
            {
                ctxLog.Debug("Collection {Collection} is already processed for every source", collection.Id);
                continue;
            }

            if (pendingSources.All(s => state[s].Budget(opts) == 0))
            {
                ctxLog.Warn(
                    "Token cap {Cap} is exhausted for every source — {Left} collections are left for the next run",
                    opts.MaxNewTokensPerRun, collections.Count - position + 1);

                totals = DiscoveryReports.Merge(totals, DiscoveryReports.Pending(collections.Count - position + 1));
                break;
            }

            using var stage = StageTimer.Start(
                ctxLog,
                $"parquet scan of {collection.Id} ({position}/{collections.Count}) for {string.Join('/', pendingSources)}");

            var scan = await ScanCollectionAsync(collection, targets, pendingSources, state, opts, ct);

            if (!scan.Completed)
                stage.Outcome("gave up");

            totals = DiscoveryReports.Merge(
                totals,
                await StoreAsync(collection, scan, pendingSources, state, opts, ct));

            if (scan.Failed)
            {
                failuresInARow++;

                var maxFailures = Math.Max(1, opts.MaxConsecutiveCollectionFailures);
                if (failuresInARow >= maxFailures)
                {
                    ctxLog.Error(
                        "{Failures} collections in a row have failed on the columnar index — it looks unavailable, "
                        + "giving up on this run. {Left} collections are left pending",
                        failuresInARow, collections.Count - position);

                    totals = DiscoveryReports.Merge(totals, DiscoveryReports.Pending(collections.Count - position));
                    break;
                }
            }
            else
            {
                failuresInARow = 0;
            }

            await DiscoveryPause.WaitAsync(ctxLog, opts.PauseBetweenCollectionsMsec, "collections", ct);
        }

        return totals;
    }

    private async Task<Dictionary<string, SourceState>> LoadStateAsync(bool full, CancellationToken ct)
    {
        using var stage = StageTimer.Start(ctxLog, "loading the registry state");

        var state = new Dictionary<string, SourceState>(StringComparer.OrdinalIgnoreCase);

        foreach (var parser in boardUrlParsers)
        {
            var processed = full
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : (await registry.GetProcessedCrawlsAsync(parser.SourceId, ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var known = (await registry.GetKnownBoardIdsAsync(parser.SourceId, ct))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            state[parser.SourceId] = new SourceState(parser, processed, known);

            ctxLog.Debug(
                "Source {Source}: {Known} known boards, {Processed} processed collections",
                parser.SourceId, known.Count, processed.Count);
        }

        return state;
    }

    private async Task<ParquetCollectionScanResult> ScanCollectionAsync(
        CrawlCollection collection,
        IReadOnlyList<BoardIndexTarget> allTargets,
        IReadOnlyList<string> pendingSources,
        Dictionary<string, SourceState> state,
        DiscoveryOptions opts,
        CancellationToken ct)
    {
        var parquetOpts = opts.Parquet;
        var fresh = pendingSources.ToDictionary(
            s => s,
            _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        // Only the sources that still need this collection are asked about - a processed one would only widen the
        // predicate and drag more files through the probes.
        var targets = allTargets.Where(t => fresh.ContainsKey(t.SourceId)).ToList();
        var tlds = targets.Select(t => t.Tld).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var hosts = targets
            .Where(t => !t.HostIsSuffix)
            .Select(t => t.Host)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var hostSuffixes = targets
            .Where(t => t.HostIsSuffix)
            .Select(t => t.Host)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var listed = await ListFilesAsync(collection, parquetOpts, ct);
        if (listed.Count == 0)
            return new ParquetCollectionScanResult { Failed = true };

        // Stage 1: the tld is the only column with usable statistics, so it is the cheapest way to drop whole files.
        var (candidates, failures) = await ProbeAsync(
            $"tld probe of {collection.Id}", collection, listed,
            new ParquetFileProbe { Files = [], Tlds = tlds, FetchStatus = parquetOpts.FetchStatus },
            parquetOpts, ct);

        // Stage 2: the host column is wider but still nothing next to the paths, and it is what leaves a handful.
        if (candidates.Count > 0 && failures < Math.Max(1, parquetOpts.MaxBatchFailuresPerCollection))
        {
            var (narrowed, hostFailures) = await ProbeAsync(
                $"host probe of {collection.Id}", collection, candidates,
                new ParquetFileProbe
                {
                    Files = [],
                    Tlds = tlds,
                    Hosts = hosts,
                    HostSuffixes = hostSuffixes,
                    FetchStatus = parquetOpts.FetchStatus
                },
                parquetOpts, ct);

            candidates = narrowed;
            failures += hostFailures;
        }

        if (failures >= Math.Max(1, parquetOpts.MaxBatchFailuresPerCollection))
        {
            ctxLog.Warn(
                "{Failures} probe batches of {Collection} have failed — abandoning the collection, it stays pending",
                failures, collection.Id);

            return Result(0, fresh, listed.Count, candidates.Count, 0, failed: true, capReached: false);
        }

        ctxLog.Info(
            "Collection {Collection}: {Selected} of {Listed} parquet files hold {Hosts}",
            collection.Id, candidates.Count, listed.Count,
            string.Join(", ", targets.Select(t => t.HostLabel).Distinct(StringComparer.OrdinalIgnoreCase)));

        if (candidates.Count == 0)
            return Result(0, fresh, listed.Count, 0, 0, failed: false, capReached: false);

        // Stage 3: the paths themselves, on the few files that are left.
        var collector = new TokenCollector(
            new HostParserIndex(targets, sourceId => state[sourceId].Parser),
            state,
            fresh,
            opts);

        var batches = candidates.Chunk(Math.Max(1, parquetOpts.FilesPerQuery)).ToList();

        long records = 0;
        var scanned = 0;

        for (var i = 0; i < batches.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var batch = batches[i];

            var query = new ParquetIndexQuery
            {
                Files = batch,
                Targets = targets,
                FetchStatus = parquetOpts.FetchStatus,
                PathSegments = parquetOpts.UrlPathSegments
            };

            using var stage = StageTimer.Start(
                ctxLog,
                $"board urls of {collection.Id}, batch {i + 1}/{batches.Count} ({batch.Length} files)");

            try
            {
                records += await parquet.ScanAsync(query, collector.Add, ct);
                scanned += batch.Length;
            }
            catch (Exception ex) when (CrawlIndexFailure.IsTransient(ex))
            {
                stage.Outcome("failed");
                failures++;

                ctxLog.Warn(
                    ex,
                    "Board url batch {Batch}/{Batches} of {Collection} has failed ({Reason})",
                    i + 1, batches.Count, collection.Id, CrawlIndexFailure.Describe(ex));

                if (failures >= Math.Max(1, parquetOpts.MaxBatchFailuresPerCollection))
                {
                    ctxLog.Warn(
                        "{Failures} batches of {Collection} have failed — abandoning the collection, it stays pending",
                        failures, collection.Id);

                    return Result(records, fresh, listed.Count, candidates.Count, scanned, true, collector.CapReached);
                }

                continue;
            }

            ctxLog.Info(
                "Collection {Collection}: {Scanned}/{Total} selected files read, {Records} distinct urls, "
                + "{Tokens} unknown tokens so far",
                collection.Id, scanned, candidates.Count, records, fresh.Sum(f => f.Value.Count));

            if (collector.CapReached)
            {
                ctxLog.Warn(
                    "Token cap {Cap} reached on {Collection} — the rest is left for the next run",
                    opts.MaxNewTokensPerRun, collection.Id);

                break;
            }

            await DiscoveryPause.WaitAsync(ctxLog, parquetOpts.PauseBetweenBatchesMsec, "parquet batches", ct);
        }

        return Result(
            records, fresh, listed.Count, candidates.Count, scanned, failures > 0, collector.CapReached);
    }

    private async Task<IReadOnlyList<string>> ListFilesAsync(
        CrawlCollection collection,
        ParquetIndexOptions opts,
        CancellationToken ct)
    {
        using var stage = StageTimer.Start(ctxLog, $"resolving parquet files of {collection.Id}");

        IReadOnlyList<string> listed;

        try
        {
            listed = await files.GetFilesAsync(collection, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stage.Outcome("failed");

            ctxLog.Warn(
                ex,
                "Parquet file listing of {Collection} is unavailable ({Reason}) — the collection stays pending",
                collection.Id, CrawlIndexFailure.Describe(ex));

            return [];
        }

        if (opts.MaxFilesPerCollection <= 0 || listed.Count <= opts.MaxFilesPerCollection)
            return listed;

        ctxLog.Warn(
            "Collection {Collection} has {Files} parquet files, only {Cap} are read (MaxFilesPerCollection)",
            collection.Id, listed.Count, opts.MaxFilesPerCollection);

        return listed.Take(opts.MaxFilesPerCollection).ToList();
    }

    /// <summary>One narrowing stage, batched so that a throttled query costs one batch instead of the whole set.</summary>
    private async Task<(IReadOnlyList<string> Matched, int Failures)> ProbeAsync(
        string label,
        CrawlCollection collection,
        IReadOnlyList<string> candidates,
        ParquetFileProbe template,
        ParquetIndexOptions opts,
        CancellationToken ct)
    {
        var stageLbl = $"{label} over {candidates.Count} files";

        using var stage = StageTimer.Start(ctxLog, stageLbl);

        var batches = candidates.Chunk(Math.Max(1, opts.FilesPerQuery)).ToList();
        var matched = new List<string>();
        var failures = 0;
        var probed = 0;

        for (var i = 0; i < batches.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var batch = batches[i];

            try
            {
                matched.AddRange(await parquet.ProbeFilesAsync(template with { Files = batch }, ct));
                probed += batch.Length;
            }
            catch (Exception ex) when (CrawlIndexFailure.IsTransient(ex))
            {
                failures++;

                ctxLog.Warn(
                    ex,
                    "{Label}: Probe batch {Batch}/{Batches} of {Collection} has failed ({Reason}) — the files it covers are "
                    + "left out of this run",
                    stageLbl, i + 1, batches.Count, collection.Id, CrawlIndexFailure.Describe(ex));

                if (failures >= Math.Max(1, opts.MaxBatchFailuresPerCollection))
                {
                    stage.Outcome("gave up");
                    break;
                }

                continue;
            }

            ctxLog.Debug(
                "{Label}: {Probed}/{Total} files probed, {Matched} match ({Elapsed})",
                stageLbl, probed, candidates.Count, matched.Count, stage.Elapsed);

            await DiscoveryPause.WaitAsync(ctxLog, opts.PauseBetweenBatchesMsec, "probe batches", ct);
        }

        return (matched, failures);
    }

    private async Task<BoardDiscoveryReport> StoreAsync(
        CrawlCollection collection,
        ParquetCollectionScanResult scan,
        IReadOnlyList<string> pendingSources,
        Dictionary<string, SourceState> state,
        DiscoveryOptions opts,
        CancellationToken ct)
    {
        var added = 0;

        // Tokens found before a failure are still worth keeping - the upsert is idempotent.
        foreach (var sourceId in pendingSources)
        {
            IReadOnlyList<string> tokens = scan.TokensBySource.TryGetValue(sourceId, out var t) ? t : [];

            added += await sink.ValidateAndStoreAsync(
                sourceId, tokens, $"common-crawl-parquet:{collection.Id}", opts, ct);

            var source = state[sourceId];

            foreach (var token in tokens)
                source.Known.Add(token);

            source.NewTokens += tokens.Count;

            if (!scan.Completed)
                continue;

            await registry.MarkCrawlProcessedAsync(new CrawlIndexProgress
            {
                SourceId = sourceId,
                CollectionId = collection.Id,
                RecordsSeen = scan.Records,
                TokensFound = tokens.Count,
                BoardsAdded = added,
                ProcessedAt = clock.GetUtcNow()
            }, ct);

            source.Processed.Add(collection.Id);
        }

        if (scan.Completed)
        {
            ctxLog.Info(
                "Collection {Collection} is done and marked processed for {Sources}: {Listed} files listed, "
                + "{Selected} selected, {Records} urls, {Tokens} tokens, {Added} new boards",
                collection.Id, string.Join('/', pendingSources), scan.FilesListed, scan.FilesSelected,
                scan.Records, scan.Tokens, added);
        }
        else
        {
            ctxLog.Warn(
                "Collection {Collection} stays pending ({Status}): {Scanned}/{Selected} of {Listed} files read, "
                + "{Records} urls, {Tokens} tokens, {Added} new boards. The next run will walk it again",
                collection.Id, scan.Status, scan.FilesScanned, scan.FilesSelected, scan.FilesListed,
                scan.Records, scan.Tokens, added);
        }

        return new BoardDiscoveryReport(
            true,
            scan.Completed ? 1 : 0,
            scan.Records,
            scan.Tokens,
            scan.Tokens,
            added,
            scan.Failed ? 1 : 0,
            scan.Completed ? 0 : 1);
    }

    private static ParquetCollectionScanResult Result(
        long records,
        Dictionary<string, HashSet<string>> fresh,
        int listed,
        int selected,
        int scanned,
        bool failed,
        bool capReached) =>
        new()
        {
            Records = records,
            TokensBySource = fresh.ToDictionary(
                f => f.Key,
                f => (IReadOnlyList<string>)f.Value.ToList(),
                StringComparer.OrdinalIgnoreCase),
            FilesListed = listed,
            FilesSelected = selected,
            FilesScanned = scanned,
            Failed = failed,
            CapReached = capReached
        };

    /// <summary>
    /// Turns the urls of one scan into unknown board tokens. The host says which ATS a url belongs to, so exactly
    /// one parser is asked per url instead of all of them.
    /// </summary>
    private sealed class TokenCollector(
        HostParserIndex parserByHost,
        IReadOnlyDictionary<string, SourceState> state,
        IReadOnlyDictionary<string, HashSet<string>> fresh,
        DiscoveryOptions opts)
    {
        /// <summary>Every source has spent its per-run budget - reading further would only throw tokens away.</summary>
        public bool CapReached { get; private set; }

        public void Add(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return;

            if (!parserByHost.TryGet(uri.Host, out var parser))
                return;

            if (!parser.TryParseBoardId(url, out var boardId))
                return;

            var source = state[parser.SourceId];
            if (source.Known.Contains(boardId))
                return;

            var tokens = fresh[parser.SourceId];
            if (tokens.Count >= source.Budget(opts))
            {
                CapReached = fresh.All(f => f.Value.Count >= state[f.Key].Budget(opts));
                return;
            }

            tokens.Add(boardId);
        }
    }

    /// <summary>Per-source run state: what is already known, what is already processed and how much budget is left.</summary>
    private sealed class SourceState(IBoardUrlParser parser, HashSet<string> processed, HashSet<string> known)
    {
        public string SourceId => parser.SourceId;

        public IBoardUrlParser Parser => parser;

        public HashSet<string> Processed => processed;

        public HashSet<string> Known => known;

        public int NewTokens { get; set; }

        public int Budget(DiscoveryOptions opts) => Math.Max(0, opts.MaxNewTokensPerRun - NewTokens);
    }
}