namespace JobsPulse.Core.Model.Infrastructure;

/// <summary>A board known to exist in some ATS - the accumulative discovery registry row.</summary>
public sealed record RegisteredBoard
{
    public required string SourceId { get; init; }

    public required string BoardId { get; init; }

    public string? DisplayName { get; init; }

    /// <summary>Source-specific board parameters as json - see <see cref="BoardCandidate.Configuration"/>.</summary>
    public string? Configuration { get; init; }

    public int JobCount { get; init; }

    public string? BoardUrl { get; init; }

    /// <summary>Where the board came from: 'common-crawl:CC-MAIN-2025-30', 'bot', etc.</summary>
    public required string DiscoveredVia { get; init; }

    public DateTimeOffset DiscoveredAt { get; init; }

    public DateTimeOffset? LastValidatedAt { get; init; }

    public bool IsActive { get; init; } = true;
}
