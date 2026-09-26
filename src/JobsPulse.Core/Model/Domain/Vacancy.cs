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

    // The detail request of a known vacancy failed - description rules keep the previous verdict instead of failing
    [JsonIgnore] public bool DescriptionUnavailable { get; init; }

    // Fingerprint of the list data this vacancy was mapped from - set only when its detail was read or skipped as
    // known rejected, so a rejection can be remembered against it
    [JsonIgnore] public string? ListHash { get; init; }

    // Rejected before with the same list data and filters - its detail is not read and it is not evaluated again
    [JsonIgnore] public bool KnownRejected { get; init; }
}