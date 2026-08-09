namespace JobsPulse.Storage.PersistentModels;

public class PersistentSeenVacancy
{
    public long Id { get; set; }
    
    public required string SourceId { get; set; }
    public required string BoardId { get; set; }
    public required string PostId { get; set; }

    public string? GroupId { get; set; }
    public required string ContentHash { get; set; }
    public string? FilterHash { get; set; }
    public required string Title { get; set; }
    public string? Location { get; set; }
    public string[] Offices { get; set; } = [];
    public required string Url { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset? FirstPublishedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
}