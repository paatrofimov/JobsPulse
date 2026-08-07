using JobsPulse.Core.Model.Domain;

namespace JobsPulse.Core.Abstractions;

public interface IOutboxStorage
{
    Task<IReadOnlyList<OutboxItem>> ReadAndLeaseAsync(int max, CancellationToken ct);

    Task MarkSentAsync(IReadOnlyList<long> ids, CancellationToken ct);

    Task MarkFailedAsync(IReadOnlyList<long> ids, TimeSpan retryAfter, string error, CancellationToken ct);

    Task MarkAsDeadLetterAsync(int maxAttempts, CancellationToken ct);
}