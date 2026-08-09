using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Domain;
using JobsPulse.Storage.PersistentModels;
using Microsoft.EntityFrameworkCore;

namespace JobsPulse.Storage.Storages;

internal class OutboxStorage(IDbContextFactory<JobsPulseDbContext> factory, TimeProvider clock) : IOutboxStorage
{
    // Read and set 'lease' status to 'pending' messages ready for delivery
    public async Task<IReadOnlyList<OutboxItem>> ReadAndLeaseAsync(
        int max,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow();

        await using var dbContext = await factory.CreateDbContextAsync(ct);
        await using var tx = await dbContext.Database.BeginTransactionAsync(ct);

        var entities = await dbContext.Outbox
            .Where(x =>
                x.Status == PersistentOutboxStatus.Pending &&
                (x.NextAttemptAt == null || x.NextAttemptAt <= now))
            .Take(max)
            .ToListAsync(ct);

        foreach (var entity in entities)
            entity.Status = PersistentOutboxStatus.Leased;

        await dbContext.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return [.. entities.Select(x => x.ToDomainModel())];
    }

    public async Task MarkDeliveredAsync(IReadOnlyList<long> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
            return;

        await using var dbContext = await factory.CreateDbContextAsync(ct);

        var now = clock.GetUtcNow();

        await dbContext.Outbox
            .Where(x => ids.Contains(x.Id))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.Status, PersistentOutboxStatus.Delivered)
                    .SetProperty(x => x.SentAt, now),
                ct);
    }

    // Set 'pending' status and schedule retry
    public async Task MarkFailedAsync(IReadOnlyList<long> ids, TimeSpan retryAfter, string error, CancellationToken ct)
    {
        if (ids.Count == 0)
            return;

        await using var dbContext = await factory.CreateDbContextAsync(ct);

        var nextAttemptAt = clock.GetUtcNow().Add(retryAfter);

        await dbContext.Outbox
            .Where(x => ids.Contains(x.Id))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.Status, PersistentOutboxStatus.Pending)
                    .SetProperty(x => x.Attempts, x => x.Attempts + 1) // atomic
                    .SetProperty(x => x.NextAttemptAt, nextAttemptAt)
                    .SetProperty(x => x.LastError, error),
                ct);
    }

    // Delivered letters are pure history - the dedup key of a change never comes back
    public async Task<int> PurgeDeliveredAsync(DateTimeOffset sentBefore, CancellationToken ct)
    {
        await using var dbContext = await factory.CreateDbContextAsync(ct);

        return await dbContext.Outbox
            .Where(x =>
                x.Status == PersistentOutboxStatus.Delivered &&
                (x.SentAt == null || x.SentAt <= sentBefore))
            .ExecuteDeleteAsync(ct);
    }

    // Set 'dead' status to 'pending' letters if all attempts are exhausted
    public async Task MarkAsDeadLetterAsync(int maxAttempts, CancellationToken ct)
    {
        await using var dbContext = await factory.CreateDbContextAsync(ct);

        await dbContext.Outbox
            .Where(x =>
                x.Status == PersistentOutboxStatus.Pending &&
                x.Attempts >= maxAttempts)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.Status, PersistentOutboxStatus.Dead),
                ct);
    }
}