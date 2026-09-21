namespace JobsPulse.Storage.PersistentModels;

public class PersistentDiscoveryCheckpoint
{
    public long Id { get; set; }

    public int Iteration { get; set; }

    /// <summary>`full` is a reserved word in SQL, so the column is `is_full`.</summary>
    public bool IsFull { get; set; }

    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }

    public string? StartedFromCollectionId { get; set; }
    public string? ResumeFromCollectionId { get; set; }

    public int CollectionsTotal { get; set; }
    public int CollectionsDone { get; set; }
    public int CollectionsProcessed { get; set; }
    public int CollectionsFailed { get; set; }

    public long RecordsSeen { get; set; }
    public int TokensFound { get; set; }
    public int BoardsAdded { get; set; }
}
