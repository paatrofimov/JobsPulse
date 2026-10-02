using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Storage.PersistentModels;
using Microsoft.EntityFrameworkCore;

namespace JobsPulse.Storage.Storages;

/// <summary>
/// `watchlist_event`. The rows of the pipeline are written by <see cref="StateStore.CommitAsync"/>, next to the outbox;
/// <see cref="AppendAsync"/> is only for restored history.
/// </summary>
internal class WatchlistEventStorage(IDbContextFactory<JobsPulseDbContext> factory) : IWatchlistEventStorage
{
    public async Task<IReadOnlyList<WatchlistEvent>> LoadAsync(
        long watchlistId,
        DateTimeOffset until,
        CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var rows = await db.WatchlistEvents
            .AsNoTracking()
            .Where(x => x.WatchlistId == watchlistId && x.OccurredAt <= until)
            .OrderBy(x => x.OccurredAt)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);

        return [.. rows.Select(x => x.ToDomainModel())];
    }

    public async Task<int> AppendAsync(IReadOnlyList<WatchlistEvent> events, CancellationToken ct)
    {
        if (events.Count == 0)
            return 0;

        await using var db = await factory.CreateDbContextAsync(ct);

        db.WatchlistEvents.AddRange(events.Select(e => new PersistentWatchlistEvent
        {
            WatchlistId = e.WatchlistId,
            SourceId = e.SourceId,
            BoardId = e.BoardId,
            PostId = e.PostId,
            CompanyName = e.CompanyName,
            ChangeKind = e.Kind,
            Location = e.Location,
            OccurredAt = e.OccurredAt,
            RunId = e.RunId
        }));

        return await db.SaveChangesAsync(ct);
    }
}
