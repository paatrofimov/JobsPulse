namespace JobsPulse.Storage.PersistentModels;

internal class PersistentSeenVacancy
{
    public required string SourceId { get; set; }
    public required string BoardId { get; set; }
    public required string PostId { get; set; }

    public string? GroupId { get; set; }
    public required string ContentHash { get; set; }
    public required string Title { get; set; }
    public string? Location { get; set; }
    public required string Url { get; set; }

    public required string VacancyPayload { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
}