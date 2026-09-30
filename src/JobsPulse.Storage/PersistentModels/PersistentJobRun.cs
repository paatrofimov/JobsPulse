namespace JobsPulse.Storage.PersistentModels;

public class PersistentJobRun
{
    public long Id { get; set; }

    public required string Role { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>`JobRunOutcome` by name - readable in a plain `SELECT`.</summary>
    public required string Outcome { get; set; }

    public string? Error { get; set; }

    /// <summary>`JobRunSummary` as jsonb - read and written whole, never queried by field.</summary>
    public string? Summary { get; set; }
}
