using JobsPulse.Core.Model.Domain;

namespace JobsPulse.Core.Model.Infrastructure;

public sealed record StateCommit
{
    public required string SourceId { get; init; }
    public required string BoardId { get; init; }

    public required IReadOnlyList<Vacancy> Upserts { get; init; }
    public required IReadOnlyList<string> ClosedPostIds { get; init; }
    public required IReadOnlyList<OutboxItem> Notifications { get; init; }

    /// <summary>Hash of the filter the upserted vacancies passed - stored per row to detect filter changes.</summary>
    public string? FilterHash { get; init; }
}