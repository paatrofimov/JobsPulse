namespace JobsPulse.Core.Model.Infrastructure;

/// <summary>One run of a one-shot job (`job_run_history`), kept so the bot can tell when each routine last ran.</summary>
public sealed record JobRun
{
    public long Id { get; init; }

    public required string Role { get; init; }

    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? FinishedAt { get; init; }

    public JobRunOutcome Outcome { get; init; }

    public string? Error { get; init; }

    public JobRunSummary Summary { get; init; } = JobRunSummary.None;
}
