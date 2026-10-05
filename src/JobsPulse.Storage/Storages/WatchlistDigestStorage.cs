using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Storage.PersistentModels;
using Microsoft.EntityFrameworkCore;

namespace JobsPulse.Storage.Storages;

/// <summary>`watchlist_digest`. Pure EF: a handful of rows per watchlist and run.</summary>
internal class WatchlistDigestStorage(
    IDbContextFactory<JobsPulseDbContext> factory,
    TimeProvider clock) : IWatchlistDigestStorage
{
    public async Task<WatchlistDigest?> GetAsync(long id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var row = await db.WatchlistDigests
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        return row?.ToDomainModel();
    }

    public async Task<WatchlistDigest?> GetLastDeliveredAsync(long watchlistId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var row = await db.WatchlistDigests
            .AsNoTracking()
            .Where(x => x.WatchlistId == watchlistId && x.DeliveredAt != null)
            .OrderByDescending(x => x.PeriodTo)
            .FirstOrDefaultAsync(ct);

        return row?.ToDomainModel();
    }

    public async Task<WatchlistDigest> AddAsync(
        long watchlistId,
        DateTimeOffset from,
        DateTimeOffset to,
        long? previousId,
        CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var row = new PersistentWatchlistDigest
        {
            WatchlistId = watchlistId,
            PeriodFrom = from,
            PeriodTo = to,
            PreviousId = previousId,
            CreatedAt = clock.GetUtcNow()
        };

        db.WatchlistDigests.Add(row);
        await db.SaveChangesAsync(ct);

        return row.ToDomainModel();
    }

    public async Task MarkDeliveredAsync(long id, DateTimeOffset deliveredAt, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        await db.WatchlistDigests
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.DeliveredAt, deliveredAt), ct);
    }
}
