namespace JobsPulse.Core.Model.Infrastructure;

public sealed record SourceTarget
{
    public required string SourceId { get; init; }
    public required string BoardId { get; init; }

    public bool IncludeDescriptions { get; init; }
}