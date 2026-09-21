namespace JobsPulse.Storage.PersistentModels;

public class PersistentBoardPollState
{
    public long Id { get; set; }

    public required string SourceId { get; set; }
    public required string BoardId { get; set; }

    public DateTimeOffset LastPolledAt { get; set; }
}
