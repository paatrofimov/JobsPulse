namespace JobsPulse.Sources.SuccessFactors.Models;

/// <summary>
/// What the legacy career portal of a tenant reports about itself. The branded domain is the interesting part: it is
/// the only way to get from a tenant - which is all a crawled url gives - to the site that actually publishes the
/// jobs, because branded domains cannot be enumerated and are named after the company, not after the tenant.
/// </summary>
public sealed record SuccessFactorsSiteIdentity
{
    /// <summary>The career site builder domain the portal points its own links at. Null for a legacy-only tenant.</summary>
    public string? Domain { get; init; }

    /// <summary>The portal answered - the tenant exists on this data center host.</summary>
    public required bool TenantExists { get; init; }

    public static SuccessFactorsSiteIdentity None => new() { TenantExists = false };
}
