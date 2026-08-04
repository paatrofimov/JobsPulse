using JobsPulse.Core.Model;

namespace JobsPulse.Core.Abstractions;

/// <summary>Снимок того, что мы уже видели по конкретному борду.</summary>
public sealed record SeenVacancy
{
    public required string ExternalId { get; init; }
    public required string ContentHash { get; init; }
    public required string Title { get; init; }
    public required string Url { get; init; }
    public string? Location { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset LastSeenAt { get; init; }
}

/// <summary>
/// Состояние + outbox в одном хранилище, потому что записывать их надо ОДНОЙ транзакцией.
/// Иначе возможен разрыв: вакансия помечена «виденной», а уведомление не поставлено в очередь —
/// и оно потеряно навсегда.
/// </summary>
public interface IStateStore
{
    Task InitializeAsync(CancellationToken ct);

    Task<IReadOnlyDictionary<string, SeenVacancy>> LoadSeenAsync(
        string sourceId, string boardKey, CancellationToken ct);

    /// <summary>Атомарно: обновить состояние борда и положить уведомления в outbox.</summary>
    Task CommitAsync(StateCommit commit, CancellationToken ct);
}

public sealed record StateCommit
{
    public required string SourceId { get; init; }
    public required string BoardKey { get; init; }

    /// <summary>Вакансии, которые есть сейчас: вставить или обновить.</summary>
    public required IReadOnlyList<Vacancy> Upserts { get; init; }

    /// <summary>ExternalId, пропавшие с борда. Заполняется только при полном фетче.</summary>
    public required IReadOnlyList<string> Closed { get; init; }

    /// <summary>Уведомления к отправке. При засеве пустой — состояние пишем, сообщений не шлём.</summary>
    public required IReadOnlyList<OutboxItem> Notifications { get; init; }
}
