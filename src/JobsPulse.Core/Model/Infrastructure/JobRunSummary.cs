namespace JobsPulse.Core.Model.Infrastructure;

/// <summary>
/// What one run of a one-shot job did, in the numbers an operator reads. Every field is optional: each job fills the
/// ones that mean something for it, and a run that failed midway may have none.
/// </summary>
public sealed record JobRunSummary
{
    public static readonly JobRunSummary None = new();

    public int? BoardsProcessed { get; init; }

    public int? BoardsFailed { get; init; }

    public long? VacanciesFetched { get; init; }

    public int? VacanciesStored { get; init; }

    public int? Changes { get; init; }

    public int? CollectionsProcessed { get; init; }

    public int? CollectionsFailed { get; init; }

    public int? CollectionsPending { get; init; }

    public long? RecordsSeen { get; init; }

    public int? TokensFound { get; init; }

    public int? BoardsAdded { get; init; }
}
