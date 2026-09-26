namespace JobsPulse.Storage.PersistentModels;

public class PersistentRejectedPosting
{
    public long Id { get; set; }

    public required string SourceId { get; set; }
    public required string BoardId { get; set; }
    public required string PostId { get; set; }

    public required string ListHash { get; set; }
    public required string FilterHash { get; set; }

    public DateTimeOffset RejectedAt { get; set; }
}
