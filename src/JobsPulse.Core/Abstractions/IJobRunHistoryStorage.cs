using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Core.Abstractions;

/// <summary>
/// The history of the one-shot jobs (`job_run_history`): when each one ran, how it ended and what it did. The jobs run
/// on GitHub runners and the bot on another host, so this table is the only way the bot can tell an operator about
/// them. Not to be confused with <see cref="ITraversalRunStorage"/> - that one coordinates deliveries between jobs in
/// flight and forgets a run a day after it ended.
/// </summary>
public interface IJobRunHistoryStorage
{
    /// <summary>Records a started run and returns its id. Runs older than the retention are dropped on the way.</summary>
    Task<long> StartAsync(string role, CancellationToken ct);

    Task FinishAsync(long id, JobRunOutcome outcome, string? error, JobRunSummary summary, CancellationToken ct);

    /// <summary>The runs of the retention window, newest first.</summary>
    Task<IReadOnlyList<JobRun>> ListRecentAsync(CancellationToken ct);
}
