namespace JobsPulse.Core.Model.Infrastructure;

public sealed record SourceTarget
{
    public required string SourceId { get; init; }
    public required string BoardId { get; init; }

    /// <summary>
    /// Source-specific board parameters as json - see <see cref="BoardCandidate.Configuration"/>. A source that
    /// needs more than <see cref="BoardId"/> reads them from here instead of parsing the id.
    /// </summary>
    public string? Configuration { get; init; }

    /// <summary>
    /// Should be enabled if any filter is configured by description contents.
    /// </summary>
    public bool IncludeDescriptions { get; init; } = true;
}