using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Storage.PersistentModels;

public class PersistentOutboxItem
{
    public long Id { get; set; }

    public required string DedupKey { get; set; }

    public required VacancyChangeKind ChangeKind { get; set; }
    public required string CompanyName { get; set; }

    // can not use join instead because seen vacancies are mutable and outbox must contain immutable snapshot of sent vacancy
    public required string VacancyPayload { get; set; }

    public PersistentOutboxStatus Status { get; set; } = PersistentOutboxStatus.Pending;

    public int Attempts { get; set; }

    public DateTimeOffset? NextAttemptAt { get; set; }
    public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
}