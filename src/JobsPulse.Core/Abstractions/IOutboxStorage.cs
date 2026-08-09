using JobsPulse.Core.Model.Domain;

namespace JobsPulse.Core.Abstractions;

public interface IOutboxStorage
{
    Task<IReadOnlyList<OutboxItem>> ReadAndLeaseAsync(int max, CancellationToken ct);

    Task MarkDeliveredAsync(IReadOnlyList<long> ids, CancellationToken ct);

    Task MarkFailedAsync(IReadOnlyList<long> ids, TimeSpan retryAfter, string error, CancellationToken ct);

    Task MarkAsDeadLetterAsync(int maxAttempts, CancellationToken ct);

    /// <summary>Deletes already delivered notifications sent before the threshold. Returns the number of rows.</summary>
    Task<int> PurgeDeliveredAsync(DateTimeOffset sentBefore, CancellationToken ct);
}