using JobsPulse.Core.Infrastructure;
using JobsPulse.Core.Model.Domain;

namespace JobsPulse.Core.Abstractions;

public interface IOutboxStorage
{
    /// <summary>
    /// Leases up to <paramref name="max"/> due notifications enqueued before <paramref name="createdBefore"/>,
    /// oldest first. The cutoff is what keeps a delivery window whole: an item of a window still being filled is
    /// left pending instead of being sent on its own - see <see cref="DeliveryWindow"/>.
    /// </summary>
    Task<IReadOnlyList<OutboxItem>> ReadAndLeaseAsync(int max, DateTimeOffset createdBefore, CancellationToken ct);

    /// <summary>How many notifications are waiting to be delivered - due ones included. Nothing is leased.</summary>
    Task<int> CountPendingAsync(CancellationToken ct);

    Task MarkDeliveredAsync(IReadOnlyList<long> ids, CancellationToken ct);

    Task MarkFailedAsync(IReadOnlyList<long> ids, TimeSpan retryAfter, string error, CancellationToken ct);

    Task MarkAsDeadLetterAsync(int maxAttempts, CancellationToken ct);

    /// <summary>Deletes already delivered notifications sent before the threshold. Returns the number of rows.</summary>
    Task<int> PurgeDeliveredAsync(DateTimeOffset sentBefore, CancellationToken ct);
}