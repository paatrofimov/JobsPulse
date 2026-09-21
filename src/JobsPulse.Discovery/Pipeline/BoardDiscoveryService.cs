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
/// Mines crawl indexes for ATS board urls and keeps the accumulative board registry up to date. This class only
/// decides which index is read and in what order; the reading itself lives in the passes, and everything
/// ATS-specific comes from <see cref="IBoardUrlParser"/> implementations.
/// </summary>
public sealed class BoardDiscoveryService(
    ICrawlIndexClient index,
    IBoardRegistryStorage registry,
    ParquetIndexDiscoveryPass parquetPass,
    HttpIndexDiscoveryPass httpPass,
    DiscoveryCheckpointTracker checkpoints,
    IOptionsMonitor<DiscoveryOptions> options,
    TimeProvider clock,
    ILog log) : IBoardDiscoveryService
{
    private readonly ILog ctxLog = log.ForContext<BoardDiscoveryService>();
    private readonly SemaphoreSlim runGate = new(1, 1);

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
            // A shutdown or a failure must not throw the walk away - whatever the offset stands at is written.
            await checkpoints.FlushAsync(true, CancellationToken.None);

            runGate.Release();
        }
    }

    public async Task<DiscoveryProgress> GetProgressAsync(CancellationToken ct)
    {
        // A run in progress holds the gate - the same trick the run itself uses to refuse a second one.
        var running = runGate.CurrentCount == 0;
        var processed = await registry.CountProcessedCrawlsBySourceAsync(ct);
        var (current, previous) = await checkpoints.ReadAsync(ct);

        return new DiscoveryProgress
        {
            IsRunning = running,
            ProcessedBySource = processed,
            Current = current,
            Previous = previous
        };
    }

    private async Task<BoardDiscoveryReport> RunCoreAsync(bool full, CancellationToken ct)
    {
        var opts = options.CurrentValue;

        if (opts.Mode == DiscoveryMode.None)
        {
            ctxLog.Warn("No discovery mode is enabled (Discovery:Mode) — nothing to do");
            return BoardDiscoveryReport.Empty;
        }

        using var run = StageTimer.Start(ctxLog, $"discovery run ({(full ? "bootstrap" : "incremental")}, mode {opts.Mode})");

        IReadOnlyList<CrawlCollection> collections;

        using (StageTimer.Start(ctxLog, "reading the crawl collection list"))
        {
            collections = await index.GetCollectionsAsync(ct);
        }

        if (collections.Count == 0)
        {
            ctxLog.Warn("Crawl index has returned no collections — nothing to discover");
            return BoardDiscoveryReport.Empty;
        }

        var window = SelectCollections(collections, full, opts);

        ctxLog.Info(
            "Discovery window: {Count} of {Total} collections ({First} … {Last})",
            window.Count, collections.Count, window.FirstOrDefault()?.Id, window.LastOrDefault()?.Id);

        // The offset of the iteration decides where the walk actually starts - a restart continues the window it
        // was cut short in instead of re-reading everything the last process had already got through.
        var checkpoint = await checkpoints.BeginAsync(full, window, opts, ct);
        var remaining = Resume(window, checkpoint);

        if (remaining.Count < window.Count)
        {
            ctxLog.Info(
                "Iteration {Iteration} continues from {Resume}: {Remaining} of {Total} collections are left",
                checkpoint.Iteration, checkpoint.ResumeFromCollectionId, remaining.Count, window.Count);
        }

        var report = BoardDiscoveryReport.Empty;

        if (opts.Mode.HasFlag(DiscoveryMode.Parquet))
            report = DiscoveryReports.Merge(report, await parquetPass.RunAsync(remaining, full, opts, ct));

        // The http pass reads `crawl_index_state`, so whatever parquet has finished is skipped here for free -
        // both when http is a mode of its own and when it is only the fallback for a failed parquet collection.
        var fallback = opts.Mode.HasFlag(DiscoveryMode.Parquet)
                       && opts.Parquet.FallbackToHttp
                       && report.CollectionsPending + report.CollectionsFailed > 0;

        if (opts.Mode.HasFlag(DiscoveryMode.Http) || fallback)
        {
            if (fallback && !opts.Mode.HasFlag(DiscoveryMode.Http))
            {
                ctxLog.Warn(
                    "{Pending} collections are left pending by the columnar index ({Failed} failed) — "
                    + "falling back to the http index",
                    report.CollectionsPending, report.CollectionsFailed);

                // The pending count is re-measured by the fallback pass; keeping the old one would double-count it.
                report = report with { CollectionsPending = 0 };
            }

            report = DiscoveryReports.Merge(report, await httpPass.RunAsync(remaining, full, opts, ct));
        }

        await checkpoints.CompleteAsync(ct);

        ctxLog.Info(
            "Discovery finished in {Elapsed}: {Collections} indexes scanned, {Failed} failed, {Pending} left pending, "
            + "{Records} records, {Tokens} tokens, {Added} new boards",
            run.Elapsed, report.CollectionsProcessed, report.CollectionsFailed, report.CollectionsPending,
            report.RecordsSeen, report.TokensFound, report.BoardsAdded);

        return report;
    }

    /// <summary>
    /// Drops everything the iteration has already walked. An offset naming a collection the window no longer holds
    /// is ignored rather than guessed at - the whole window is walked again and `crawl_index_state` keeps that cheap.
    /// </summary>
    private static IReadOnlyList<CrawlCollection> Resume(
        IReadOnlyList<CrawlCollection> window,
        DiscoveryCheckpoint checkpoint)
    {
        if (checkpoint.ResumeFromCollectionId is not { } resume)
            return window;

        var index = window
            .Select((c, i) => (c.Id, Index: i))
            .Where(x => string.Equals(x.Id, resume, StringComparison.OrdinalIgnoreCase))
            .Select(x => (int?)x.Index)
            .FirstOrDefault();

        return index is null ? window : window.Skip(index.Value).ToList();
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
}
