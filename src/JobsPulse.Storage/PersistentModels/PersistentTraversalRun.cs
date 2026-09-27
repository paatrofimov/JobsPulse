namespace JobsPulse.Storage.PersistentModels;

public class PersistentTraversalRun
{
    public long Id { get; set; }

    public required string Role { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset HeartbeatAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }
}
