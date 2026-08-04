using JobsPulse.Core.Model;

namespace JobsPulse.Core.Abstractions;

/// <summary>
/// Единица доставки. Кладётся в той же транзакции, что и обновление состояния (transactional outbox).
/// Гарантия — at-least-once: дубль сообщения лучше потерянной вакансии.
/// </summary>
public sealed record OutboxItem
{
    public long Id { get; init; }

    /// <summary>Ключ идемпотентности: (изменение + хеш контента). Защищает от повторной постановки.</summary>
    public required string DedupKey { get; init; }

    public required string ChatId { get; init; }
    public bool Silent { get; init; }

    public required ChangeKind Kind { get; init; }
    public required string CompanyName { get; init; }
    public required Vacancy Vacancy { get; init; }

    public int Attempts { get; init; }
    public DateTimeOffset? NextAttemptAt { get; init; }
}

public interface IOutbox
{
    /// <summary>Забрать пачку готовых к отправке (с учётом backoff) и пометить как «в работе».</summary>
    Task<IReadOnlyList<OutboxItem>> LeaseAsync(int max, CancellationToken ct);

    Task MarkSentAsync(IReadOnlyList<long> ids, CancellationToken ct);

    /// <summary>Вернуть в очередь с отложенным повтором. После N попыток — в dead-letter.</summary>
    Task MarkFailedAsync(IReadOnlyList<long> ids, TimeSpan retryAfter, string error, CancellationToken ct);
}
