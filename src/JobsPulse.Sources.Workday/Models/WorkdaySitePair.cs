namespace JobsPulse.Sources.Workday.Models;

/// <summary>The tenant and site as the careers page itself reports them.</summary>
public sealed record WorkdaySitePair
{
    public required string Tenant { get; init; }

    public required string Site { get; init; }
}
