namespace JobsPulse.Core.Model.Infrastructure;

public sealed record BoardCandidate
{
    public required string SourceId { get; init; }
    public required string BoardId { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>
    /// Source-specific board parameters as json, for ATS that a single slug cannot address - Workday needs
    /// host, tenant and site. Null for every source whose <see cref="BoardId"/> is the whole address.
    /// </summary>
    public string? Configuration { get; init; }

    public int JobCount { get; init; }
    public string? BoardUrl { get; init; }

    public ResolutionKind Resolution { get; init; }
}