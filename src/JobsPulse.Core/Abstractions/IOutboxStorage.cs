using JobsPulse.Core.Infrastructure;
using JobsPulse.Core.Model.Domain;

namespace JobsPulse.Core.Abstractions;

public interface IOutboxStorage
{
    /// <summary>
    /// Leases up to <paramref name="max"/> due notifications enqueued before <paramref name="createdBefore"/>,
    /// oldest first and whole <paramref name="window"/>s only. The cutoff keeps the window still being filled
    /// pending; the whole-window cut keeps a closed window from being split across batches by the cap - either way
    /// a window would arrive as several messages under the same header. See <see cref="DeliveryWindow"/>.
    /// </summary>
    Task<IReadOnlyList<OutboxItem>> ReadAndLeaseAsync(
        int max,
        DateTimeOffset createdBefore,
        TimeSpan window,
        CancellationToken ct);

    Task MarkDeliveredAsync(IReadOnlyList<long> ids, CancellationToken ct);

    Task MarkFailedAsync(IReadOnlyList<long> ids, TimeSpan retryAfter, string error, CancellationToken ct);

    Task MarkAsDeadLetterAsync(int maxAttempts, CancellationToken ct);

    /// <summary>Deletes already delivered notifications sent before the threshold. Returns the number of rows.</summary>
    Task<int> PurgeDeliveredAsync(DateTimeOffset sentBefore, CancellationToken ct);
}