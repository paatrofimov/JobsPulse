using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Discovery.Models;
using JobsPulse.Discovery.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Discovery.Infrastructure;

/// <summary>
/// The offset of the running discovery iteration. A run takes hours and a restart used to throw the whole walk
/// away: `crawl_index_state` only remembers collections that were finished for every source, so everything the
/// current collection had done was read again. This keeps the position and the accumulated counters in
/// `discovery_checkpoint` instead, written at most every <c>CheckpointIntervalMinutes</c> - often enough that a
/// restart costs one collection, rare enough that a walk of hundreds of batches is not a write loop.
///
/// It is also the read side of «which iteration is this and how much has it done» - see
/// <see cref="ReadAsync"/>, which prefers the live in-memory numbers over the last written row.
/// </summary>
public sealed class DiscoveryCheckpointTracker(
    IDiscoveryCheckpointStorage storage,
    TimeProvider clock,
    ILog log)
{
    private readonly ILog ctxLog = log.ForContext<DiscoveryCheckpointTracker>();
    private readonly object gate = new();

    private List<string> window = [];
    private int offset;
    private DiscoveryCheckpoint? current;
    private DateTimeOffset lastSavedAt;
    private TimeSpan interval = TimeSpan.FromMinutes(5);
    private bool dirty;

    /// <summary>
    /// Opens an iteration - or picks the unfinished one up - and answers with the offset it stands at. The caller
    /// walks <see cref="DiscoveryCheckpoint.ResumeFromCollectionId"/> onwards.
    /// </summary>
    public async Task<DiscoveryCheckpoint> BeginAsync(
        bool full,
        IReadOnlyList<CrawlCollection> collections,
        DiscoveryOptions opts,
        CancellationToken ct)
    {
        var ids = collections.Select(c => c.Id).ToList();
        var now = clock.GetUtcNow();
        var latest = (await storage.GetLatestAsync(1, ct)).FirstOrDefault();

        var position = 0;
        var resumed = false;

        // An unfinished iteration is only resumable over the same kind of window - a bootstrap and an incremental
        // run do not walk the same collections, so their offsets do not mean the same thing.
        if (latest is { IsFinished: false } && latest.Full == full)
        {
            position = Position(ids, latest.ResumeFromCollectionId);

            if (position >= ids.Count)
            {
                // Nothing is left of the window it stopped in: the row is stale, not resumable.
                await CloseStaleAsync(latest, now, ct);
            }
            else
            {
                resumed = true;
            }
        }

        DiscoveryCheckpoint checkpoint;

        if (resumed)
        {
            checkpoint = latest! with
            {
                UpdatedAt = now,
                CollectionsTotal = ids.Count,
                CollectionsDone = position,
                ResumeFromCollectionId = At(ids, position)
            };
        }
        else
        {
            position = 0;

            checkpoint = new DiscoveryCheckpoint
            {
                Iteration = (latest?.Iteration ?? 0) + 1,
                Full = full,
                StartedAt = now,
                UpdatedAt = now,
                StartedFromCollectionId = ids.FirstOrDefault(),
                ResumeFromCollectionId = ids.FirstOrDefault(),
                CollectionsTotal = ids.Count
            };
        }

        lock (gate)
        {
            window = ids;
            offset = position;
            current = checkpoint;
            lastSavedAt = now;
            interval = TimeSpan.FromMinutes(Math.Max(1, opts.CheckpointIntervalMinutes));
            dirty = false;
        }

        if (resumed)
        {
            ctxLog.Info(
                "Discovery iteration {Iteration} is resumed at {Resume} ({Done}/{Total} collections behind it): "
                + "{Records} urls, {Tokens} tokens, {Added} boards so far",
                checkpoint.Iteration, checkpoint.ResumeFromCollectionId, checkpoint.CollectionsDone,
                checkpoint.CollectionsTotal, checkpoint.RecordsSeen, checkpoint.TokensFound, checkpoint.BoardsAdded);
        }
        else
        {
            ctxLog.Info(
                "Discovery iteration {Iteration} starts at {Start}, {Total} collections in the window",
                checkpoint.Iteration, checkpoint.StartedFromCollectionId, checkpoint.CollectionsTotal);
        }

        await SaveAsync(checkpoint, ct);

        return checkpoint;
    }

    /// <summary>
    /// The collection is behind the walk for every source that needed it, so the offset moves past it. Only the
    /// collection-major pass may say this - see <see cref="CountersAdvancedAsync"/>.
    /// </summary>
    public Task CollectionFinishedAsync(CrawlCollection collection, BoardDiscoveryReport delta, CancellationToken ct)
    {
        lock (gate)
        {
            if (current is null)
                return Task.CompletedTask;

            var index = window.FindIndex(id => string.Equals(id, collection.Id, StringComparison.OrdinalIgnoreCase));

            // A collection may be reported out of order by a fallback pass - the offset never walks backwards.
            if (index >= 0 && index + 1 > offset)
                offset = index + 1;

            current = Accumulate(current, delta) with
            {
                CollectionsDone = offset,
                ResumeFromCollectionId = At(window, offset)
            };

            dirty = true;
        }

        return FlushAsync(false, ct);
    }

    /// <summary>
    /// Counters only. The http pass is source-major, so a collection it has finished says nothing about the
    /// position of the window - the next source still has to walk it.
    /// </summary>
    public Task CountersAdvancedAsync(BoardDiscoveryReport delta, CancellationToken ct)
    {
        lock (gate)
        {
            if (current is null)
                return Task.CompletedTask;

            current = Accumulate(current, delta);
            dirty = true;
        }

        return FlushAsync(false, ct);
    }

    /// <summary>The whole window is behind the iteration: the offset is cleared and the row is closed.</summary>
    public async Task CompleteAsync(CancellationToken ct)
    {
        DiscoveryCheckpoint? finished = null;
        var now = clock.GetUtcNow();

        lock (gate)
        {
            if (current is not null)
            {
                // `CollectionsDone` is left as the walk left it: a run that gave up on a failing index has
                // genuinely not covered its window, and the next iteration is what picks the rest up.
                current = current with
                {
                    FinishedAt = now,
                    UpdatedAt = now,
                    ResumeFromCollectionId = null
                };

                finished = current;
                dirty = false;
                lastSavedAt = now;
            }
        }

        if (finished is null)
            return;

        await SaveAsync(finished, ct);

        ctxLog.Info(
            "Discovery iteration {Iteration} is finished: {Processed} collections processed, {Failed} failed, "
            + "{Records} urls, {Tokens} tokens, {Added} new boards",
            finished.Iteration, finished.CollectionsProcessed, finished.CollectionsFailed, finished.RecordsSeen,
            finished.TokensFound, finished.BoardsAdded);
    }

    /// <summary>
    /// Writes the offset when it is due, or right now when <paramref name="force"/> - which is what a shutdown or a
    /// failed run does, so the position of a half-walked window is not lost.
    /// </summary>
    public async Task FlushAsync(bool force, CancellationToken ct)
    {
        DiscoveryCheckpoint? snapshot = null;

        lock (gate)
        {
            var now = clock.GetUtcNow();

            if (current is not null && dirty && (force || now - lastSavedAt >= interval))
            {
                current = current with { UpdatedAt = now };
                snapshot = current;
                lastSavedAt = now;
                dirty = false;
            }
        }

        if (snapshot is null)
            return;

        if (!await SaveAsync(snapshot, ct))
            return;

        ctxLog.Info(
            "Discovery offset of iteration {Iteration} saved at {Resume}: {Done}/{Total} collections, "
            + "{Records} urls, {Tokens} tokens, {Added} new boards",
            snapshot.Iteration, snapshot.ResumeFromCollectionId ?? "the end of the window", snapshot.CollectionsDone,
            snapshot.CollectionsTotal, snapshot.RecordsSeen, snapshot.TokensFound, snapshot.BoardsAdded);
    }

    /// <summary>
    /// The current iteration and the one before it, for the bot. The in-memory offset is up to one interval ahead
    /// of the stored row, so it wins whenever it is there.
    /// </summary>
    public async Task<(DiscoveryCheckpoint? Current, DiscoveryCheckpoint? Previous)> ReadAsync(CancellationToken ct)
    {
        var rows = await storage.GetLatestAsync(2, ct);

        var stored = rows.Count > 0 ? rows[0] : null;
        var previous = rows.Count > 1 ? rows[1] : null;

        DiscoveryCheckpoint? live;

        lock (gate)
        {
            live = current;
        }

        if (live is null || (stored is not null && live.Iteration < stored.Iteration))
            return (stored, previous);

        return (live, live.Iteration == stored?.Iteration ? previous : stored);
    }

    /// <summary>A write must never break the run it is only bookkeeping for.</summary>
    private async Task<bool> SaveAsync(DiscoveryCheckpoint checkpoint, CancellationToken ct)
    {
        try
        {
            await storage.SaveAsync(checkpoint, ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ctxLog.Warn(ex, "Could not save the discovery offset of iteration {Iteration}", checkpoint.Iteration);

            lock (gate)
            {
                dirty = true;
            }

            return false;
        }
    }

    private async Task CloseStaleAsync(DiscoveryCheckpoint stale, DateTimeOffset now, CancellationToken ct)
    {
        ctxLog.Warn(
            "Discovery iteration {Iteration} stopped at {Resume}, which is not in the current window — "
            + "closing it and starting a new one",
            stale.Iteration, stale.ResumeFromCollectionId);

        await SaveAsync(
            stale with
            {
                FinishedAt = now,
                UpdatedAt = now,
                ResumeFromCollectionId = null
            },
            ct);
    }

    private static DiscoveryCheckpoint Accumulate(DiscoveryCheckpoint checkpoint, BoardDiscoveryReport delta) =>
        checkpoint with
        {
            CollectionsProcessed = checkpoint.CollectionsProcessed + delta.CollectionsProcessed,
            CollectionsFailed = checkpoint.CollectionsFailed + delta.CollectionsFailed,
            RecordsSeen = checkpoint.RecordsSeen + delta.RecordsSeen,
            TokensFound = checkpoint.TokensFound + delta.TokensFound,
            BoardsAdded = checkpoint.BoardsAdded + delta.BoardsAdded
        };

    /// <summary>Where the stored offset lands in the current window; the whole window when it is not there.</summary>
    private static int Position(List<string> ids, string? collectionId)
    {
        if (string.IsNullOrWhiteSpace(collectionId))
            return 0;

        var index = ids.FindIndex(id => string.Equals(id, collectionId, StringComparison.OrdinalIgnoreCase));

        return index < 0 ? ids.Count : index;
    }

    private static string? At(IReadOnlyList<string> ids, int index) => index >= 0 && index < ids.Count ? ids[index] : null;
}
