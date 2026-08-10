namespace JobsPulse.Core.Model.Infrastructure;

public sealed record StateCommitResult(
    int UpsertVacanciesAffectedRows,
    int CloseVacanciesAffectedRows,
    int OutboxAffectedRows,
    int MatchAffectedRows = 0)
{
    public static StateCommitResult Empty => new(0, 0, 0);
}
