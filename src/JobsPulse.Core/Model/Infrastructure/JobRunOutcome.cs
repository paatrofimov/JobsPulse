namespace JobsPulse.Core.Model.Infrastructure;

public enum JobRunOutcome
{
    /// <summary>Started and not finished yet - or killed before it could say so.</summary>
    Running,

    Succeeded,

    Failed,

    /// <summary>Reached <c>Job:MaxRunMinutes</c>. Not a failure: the next run continues from the saved state.</summary>
    TimedOut,

    /// <summary>Cancelled by the host - the runner was stopped or the workflow was cancelled.</summary>
    Stopped
}
