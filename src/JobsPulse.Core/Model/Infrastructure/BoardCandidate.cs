namespace JobsPulse.Core.Model.Infrastructure;

public sealed record BoardCandidate
{
    public required string SourceId { get; init; }
    public required string BoardId { get; init; }

    public required string DisplayName { get; init; }

    public int JobCount { get; init; }
    public string? BoardUrl { get; init; }

    public ResolutionKind Resolution { get; init; }
}