using System.Text.Json.Serialization;
using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Core.Model.Domain;

public sealed record Vacancy
{
    [JsonIgnore] public VacancyKey Key => new(SourceId, BoardId, PostId);

    public required string SourceId { get; init; }

    public required string BoardId { get; init; }

    public required string PostId { get; init; }

    public string? GroupId { get; init; }

    public required string Title { get; init; }

    public string? Location { get; init; }

    public IReadOnlyList<string> Offices { get; init; } = [];

    public required string Url { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
    public DateTimeOffset? FirstPublishedAt { get; init; }

    public DateTimeOffset? FirstSeenAt { get; init; }

    public string ContentHash { get; init; } = null!;

    // excluded from db storage
    // no need to store vacancies' descriptions which can be too large
    [JsonIgnore] public string? Description { get; init; }
}