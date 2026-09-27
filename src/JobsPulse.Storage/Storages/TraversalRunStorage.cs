using JobsPulse.Core.Abstractions;
using JobsPulse.Storage.PersistentModels;
using Microsoft.EntityFrameworkCore;

namespace JobsPulse.Storage.Storages;

/// <summary>Pure EF over a table of a few rows: a couple of jobs a day write one each.</summary>
internal class TraversalRunStorage(
    IDbContextFactory<JobsPulseDbContext> factory,
    TimeProvider clock) : ITraversalRunStorage
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(1);

    public async Task<long> StartAsync(string role, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var now = clock.GetUtcNow();
        var threshold = now - Retention;

        await db.TraversalRuns
            .Where(x => x.HeartbeatAt < threshold)
            .ExecuteDeleteAsync(ct);

        var row = new PersistentTraversalRun
        {
            Role = role,
            StartedAt = now,
            HeartbeatAt = now
        };

        db.TraversalRuns.Add(row);
        await db.SaveChangesAsync(ct);

        return row.Id;
    }

    public async Task HeartbeatAsync(long id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var now = clock.GetUtcNow();

        await db.TraversalRuns
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.HeartbeatAt, now), ct);
    }

    public async Task FinishAsync(long id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var now = clock.GetUtcNow();

        await db.TraversalRuns
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.HeartbeatAt, now)
                .SetProperty(x => x.FinishedAt, now), ct);
    }

    public async Task<bool> AnyActiveAsync(DateTimeOffset aliveSince, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        return await db.TraversalRuns
            .AnyAsync(x => x.FinishedAt == null && x.HeartbeatAt >= aliveSince, ct);
    }
}
