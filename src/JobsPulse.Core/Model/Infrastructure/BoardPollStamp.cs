namespace JobsPulse.Core.Model.Infrastructure;

/// <summary>When a board was last traversed. The unit `IBoardPollStateStorage` is written in.</summary>
public readonly record struct BoardPollStamp(string SourceId, string BoardId, DateTimeOffset PolledAt)
{
    public string BoardKey => $"{SourceId}/{BoardId}";
}
