namespace JobsPulse.Sources.SuccessFactors.Models;

/// <summary>
/// What a url tells us on its own - and, as with Workday, that is a hint rather than an address.
///
/// A legacy url names its tenant outright but not the branded site the tenant actually publishes on. A branded url
/// names a domain that no url can prove belongs to SuccessFactors at all: the domain is the company's own, and the
/// only thing that settles it is asking the site. So both forms come out of parsing as a candidate, and the resolver
/// is what turns one into a board.
/// </summary>
public sealed record SuccessFactorsUrlParts
{
    /// <summary>Branded career site domain, when the url was on one.</summary>
    public string? Domain { get; init; }

    /// <summary>Data center host, when the url was a legacy one.</summary>
    public string? RcmHost { get; init; }

    /// <summary>The 'company' parameter of a legacy url - a tenant the site itself has not confirmed yet.</summary>
    public string? TenantHint { get; init; }

    public SuccessFactorsSiteVariant Variant { get; init; }

    /// <summary>The url addressed a single vacancy rather than the board.</summary>
    public bool IsJobUrl { get; init; }

    /// <summary>The posting id, when the url was a branded job url that carries one.</summary>
    public string? PostId { get; init; }

    public SuccessFactorsBoardConfig ToConfig() => new()
    {
        Domain = Domain,
        RcmHost = RcmHost,
        Tenant = TenantHint,
        Variant = Variant
    };
}
