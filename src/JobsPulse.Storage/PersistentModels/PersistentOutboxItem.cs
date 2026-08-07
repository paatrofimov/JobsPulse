using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Storage.PersistentModels;

internal class PersistentOutboxItem
{
    public required long Id { get; set; }
    
    public required string DedupKey { get; set; }

    public bool Silent { get; set; }

    public required VacancyChangeKind ChangeKind { get; set; }
    public required string CompanyName { get; set; }

    public required string VacancyPayload { get; set; }

    public PersistentOutboxStatus Status { get; set; } = PersistentOutboxStatus.Pending;
    public int Attempts { get; set; }

    public DateTimeOffset? NextAttemptAt { get; set; }
    public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
}