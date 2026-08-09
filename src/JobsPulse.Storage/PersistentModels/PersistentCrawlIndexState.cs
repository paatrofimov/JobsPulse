namespace JobsPulse.Storage.PersistentModels;

public class PersistentCrawlIndexState
{
    public long Id { get; set; }

    public required string SourceId { get; set; }
    public required string CollectionId { get; set; }

    public long RecordsSeen { get; set; }
    public int TokensFound { get; set; }
    public int BoardsAdded { get; set; }

    public DateTimeOffset ProcessedAt { get; set; }
}
