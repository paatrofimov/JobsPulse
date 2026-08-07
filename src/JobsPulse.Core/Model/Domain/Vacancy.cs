using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Core.Model.Domain;

public sealed record Vacancy
{
    public VacancyKey Key => new(SourceId, BoardId, PostId);

    public required string SourceId { get; init; }

    public required string BoardId { get; init; }

    public required string PostId { get; init; }

    public string? GroupId { get; init; }

    public required string Title { get; init; }

    public string? Location { get; init; }

    public IReadOnlyList<string> Departments { get; init; } = [];

    public IReadOnlyList<string> Offices { get; init; } = [];

    public required string Url { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public DateTimeOffset? FirstPublished { get; init; }

    public string ContentHash { get; init; } = null!;

    public string? Description { get; init; }
}