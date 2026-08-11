using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Storage.PersistentModels;
using Microsoft.EntityFrameworkCore;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Storage.Storages;

/// <summary>
/// Pure EF: the watchlist configuration is small, read whole and written one row at a time. Every mutation is
/// committed immediately - the bot has no other way to change the configuration and no cache to invalidate.
/// </summary>
internal class WatchlistStorage(
    IDbContextFactory<JobsPulseDbContext> factory,
    TimeProvider clock,
    ILog log) : IWatchlistStorage
{
    private readonly ILog ctxLog = log.ForContext<WatchlistStorage>();

    public async Task<IReadOnlyList<Watchlist>> GetEnabledAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var rows = await Query(db)
            .Where(x => x.Enabled)
            .ToListAsync(ct);

        return [.. rows.Select(x => x.ToDomainModel())];
    }

    public async Task<IReadOnlyList<Watchlist>> GetAllAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var rows = await Query(db).ToListAsync(ct);

        return [.. rows.Select(x => x.ToDomainModel())];
    }

    public async Task<Watchlist?> GetAsync(long watchlistId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var row = await Query(db).FirstOrDefaultAsync(x => x.Id == watchlistId, ct);

        return row?.ToDomainModel();
    }

    public async Task<Watchlist?> FindByNameAsync(string name, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var normalized = name.Trim().ToLowerInvariant();

        var row = await Query(db).FirstOrDefaultAsync(x => x.Name.ToLower() == normalized, ct);

        return row?.ToDomainModel();
    }

    public async Task<Watchlist?> CreateAsync(string name, FilterSpec filter, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var normalized = name.Trim();
        if (normalized.Length == 0)
            return null;

        var taken = await db.Watchlists
            .AsNoTracking()
            .AnyAsync(x => x.Name.ToLower() == normalized.ToLower(), ct);

        if (taken)
            return null;

        var now = clock.GetUtcNow();

        var row = new PersistentWatchlist
        {
            Name = normalized,
            Enabled = true,
            Filter = filter.ToJson(),
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Watchlists.Add(row);
        await db.SaveChangesAsync(ct);

        ctxLog.Info("Watchlist '{Watchlist}' created with id {Id}", row.Name, row.Id);

        return row.ToDomainModel();
    }

    public async Task<bool> DeleteAsync(long watchlistId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        // Entries and match rows are removed by the cascade - the vacancies themselves are global and stay.
        var affected = await db.Watchlists
            .Where(x => x.Id == watchlistId)
            .ExecuteDeleteAsync(ct);

        return affected > 0;
    }

    public async Task<bool> SetEnabledAsync(long watchlistId, bool enabled, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var affected = await db.Watchlists
            .Where(x => x.Id == watchlistId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Enabled, enabled)
                .SetProperty(x => x.UpdatedAt, clock.GetUtcNow()), ct);

        return affected > 0;
    }

    public async Task<bool> SetFilterAsync(long watchlistId, FilterSpec filter, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var json = filter.ToJson();

        var affected = await db.Watchlists
            .Where(x => x.Id == watchlistId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Filter, json)
                .SetProperty(x => x.UpdatedAt, clock.GetUtcNow()), ct);

        return affected > 0;
    }

    public async Task<bool> SetIntervalAsync(long watchlistId, int? intervalMinutes, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var affected = await db.Watchlists
            .Where(x => x.Id == watchlistId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.IntervalMinutesOverride, intervalMinutes)
                .SetProperty(x => x.UpdatedAt, clock.GetUtcNow()), ct);

        return affected > 0;
    }

    public async Task<WatchlistEntry?> AddEntryAsync(
        long watchlistId,
        string sourceId,
        string boardId,
        string companyName,
        string? configuration,
        CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var watchlistExists = await db.Watchlists.AnyAsync(x => x.Id == watchlistId, ct);
        if (!watchlistExists)
            return null;

        var existing = await db.WatchlistEntries
            .FirstOrDefaultAsync(
                x => x.WatchlistId == watchlistId && x.SourceId == sourceId && x.BoardId == boardId, ct);

        // Re-adding a board is a refresh: the company name and the enabled flag are updated, the row is kept.
        if (existing is not null)
        {
            existing.CompanyName = companyName;
            existing.Enabled = true;

            // A re-add refreshes the configuration, but never clears a stored one with a probe that came back empty.
            if (configuration is not null)
                existing.Configuration = configuration;

            await db.SaveChangesAsync(ct);

            return existing.ToDomainModel();
        }

        var row = new PersistentWatchlistEntry
        {
            WatchlistId = watchlistId,
            SourceId = sourceId,
            BoardId = boardId,
            CompanyName = companyName,
            Configuration = configuration,
            Enabled = true,
            CreatedAt = clock.GetUtcNow()
        };

        db.WatchlistEntries.Add(row);
        await db.SaveChangesAsync(ct);

        return row.ToDomainModel();
    }

    public async Task<bool> RemoveEntryAsync(long entryId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var affected = await db.WatchlistEntries
            .Where(x => x.Id == entryId)
            .ExecuteDeleteAsync(ct);

        return affected > 0;
    }

    public async Task<bool> SetEntryEnabledAsync(long entryId, bool enabled, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var affected = await db.WatchlistEntries
            .Where(x => x.Id == entryId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Enabled, enabled), ct);

        return affected > 0;
    }

    public async Task<int> DisableBoardAsync(string sourceId, string boardId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        return await db.WatchlistEntries
            .Where(x => x.SourceId == sourceId && x.BoardId == boardId && x.Enabled)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Enabled, false), ct);
    }

    private static IQueryable<PersistentWatchlist> Query(JobsPulseDbContext db) =>
        db.Watchlists
            .AsNoTracking()
            .Include(x => x.Entries)
            .OrderBy(x => x.Id);
}
