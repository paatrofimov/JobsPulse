namespace JobsPulse.Sources.SmartRecruiters.Infrastructure;

public static class SmartRecruitersCompanySlug
{
    // Path segments of the SmartRecruiters url scheme itself, never a company identifier.
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "v1", "companies", "postings", "publications", "oneclick-ui", "oneclick", "job", "jobs", "apply",
        "app", "identity", "oauth", "static", "assets", "images", "favicon.ico", "robots.txt", "sitemap.xml"
    };

    // Only these hosts carry a company in the path; www.smartrecruiters.com is a marketing site.
    private static readonly HashSet<string> Hosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "jobs", "careers", "api"
    };

    /// <summary>
    /// jobs.smartrecruiters.com/{company}/{postingId}, careers.smartrecruiters.com/{company},
    /// api.smartrecruiters.com/v1/companies/{company}/postings.
    /// </summary>
    public static string? ExtractFromUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;

        if (!uri.Host.EndsWith("smartrecruiters.com", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!Hosts.Contains(uri.Host.Split('.')[0]))
            return null;

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return null;

        var companiesIndex = Array.FindIndex(segments, s => s.Equals("companies", StringComparison.OrdinalIgnoreCase));
        var slug = companiesIndex >= 0 && companiesIndex + 1 < segments.Length
            ? segments[companiesIndex + 1]
            : segments[0];

        if (string.IsNullOrWhiteSpace(slug) || Reserved.Contains(slug))
            return null;

        // Company lookup is case-insensitive, so the lowercase form is stored - one board, one registry row.
        return slug.ToLowerInvariant();
    }
}
