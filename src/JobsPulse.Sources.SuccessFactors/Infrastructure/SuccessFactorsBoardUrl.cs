using System.Web;
using JobsPulse.Sources.SuccessFactors.Models;

namespace JobsPulse.Sources.SuccessFactors.Infrastructure;

/// <summary>
/// Reads a board out of any public SuccessFactors url and collapses every form of one board onto one address: the job
/// search page, a deep link to a single vacancy, a locale-prefixed page and a site mounted under a path all normalize
/// to the same <see cref="SuccessFactorsUrlParts"/>, because the career site serves all of them from its domain root.
///
/// The awkward half is that a branded career domain is the company's own and looks like any other domain, so unlike
/// every other ATS here there is no host to recognize. What is recognized instead is the shape of the path the
/// recruiting marketing platform generates ('/search/', '/job/{slug}/{id}/', '/go/{slug}/{id}/'), plus a bare domain,
/// which is what a person pastes. That deliberately over-claims - a careers page of some other vendor matches too -
/// and the probe is what settles it: a site that is not one of these has no feed to answer with.
///
/// The exception is a site the platform hosts itself ('ascendlearning.jobs.hr.cloud.sap') - there the domain does say
/// SuccessFactors, and it says career site rather than data center host, which is why
/// <see cref="SuccessFactorsBoardConfig.IsHostedCareerSite"/> is asked before <c>IsRcmHost</c>.
/// </summary>
public static class SuccessFactorsBoardUrl
{
    /// <summary>Path segments the recruiting marketing platform owns. Their presence is what marks a branded site.</summary>
    private static readonly string[] SiteMarkers =
        ["search", "job", "jobs", "go", "talentcommunity", "viewjob", "joblist"];

    /// <summary>Returns null for anything that cannot be a SuccessFactors url - every resolver is asked about every url.</summary>
    public static SuccessFactorsUrlParts? Parse(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var text = url.Trim();

        // A board is often written down without a scheme - 'jobs.sap.com' - and Uri needs one to find the host.
        if (!text.Contains("://", StringComparison.Ordinal))
            text = "https://" + text;

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
            return null;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return null;

        var host = uri.Host.ToLowerInvariant();

        if (host.Length == 0)
            return null;

        return SuccessFactorsBoardConfig.IsRcmHost(host)
            ? ParseLegacy(host, uri)
            : ParseBranded(host, uri);
    }

    /// <summary>
    /// 'career8.successfactors.com/career?company=brevardcou&amp;career_job_req_id=123', and every other path the
    /// recruiting backend serves with a 'company=' on it - '/sfcareer/jobreqcareer?jobId=32385&amp;company=kmd' is the
    /// same board. The path is ignored on purpose: only the tenant identifies anything here, and it is spelled out,
    /// which is what makes the legacy form worth mining a crawl index for - but it is still only a hint until the
    /// portal answers for it.
    /// </summary>
    private static SuccessFactorsUrlParts? ParseLegacy(string host, Uri uri)
    {
        var query = HttpUtility.ParseQueryString(uri.Query);

        var tenant = First(query["company"], query["career_company"]);

        if (string.IsNullOrWhiteSpace(tenant))
            return null;

        return new SuccessFactorsUrlParts
        {
            RcmHost = host,
            TenantHint = tenant.Trim(),
            Variant = SuccessFactorsSiteVariant.LegacyCareerPortal,
            // The portal spells the requisition out as 'career_job_req_id', the deep link as 'jobId'.
            IsJobUrl = !string.IsNullOrWhiteSpace(query["career_job_req_id"]) ||
                       !string.IsNullOrWhiteSpace(query["jobId"])
        };
    }

    private static SuccessFactorsUrlParts? ParseBranded(string host, Uri uri)
    {
        // A host with no dot is not a public career domain, whatever the path says.
        if (!host.Contains('.'))
            return null;

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var parts = new SuccessFactorsUrlParts
        {
            Domain = host,
            Variant = SuccessFactorsSiteVariant.CareerSiteBuilder
        };

        // A bare domain is the board itself - and it is what a person pastes.
        if (segments.Length == 0)
            return parts;

        var marker = Array.FindIndex(
            segments,
            s => SiteMarkers.Contains(s, StringComparer.OrdinalIgnoreCase));

        if (marker < 0)
            return null;

        // '/job/{slug}/{id}/' - the id is the segment after the slug, and the only identifier the site publishes.
        var isJob = segments[marker].Equals("job", StringComparison.OrdinalIgnoreCase);

        return parts with
        {
            IsJobUrl = isJob,
            PostId = isJob ? SuccessFactorsPostingIdentity.FromUrl(uri.AbsolutePath) : null
        };
    }

    private static string? First(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
