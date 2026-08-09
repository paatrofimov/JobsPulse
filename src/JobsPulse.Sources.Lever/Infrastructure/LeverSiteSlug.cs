namespace JobsPulse.Sources.Lever.Infrastructure;

public static class LeverSiteSlug
{
    // Path segments of the Lever url scheme itself, never a company site.
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "v0", "v1", "postings", "apply", "static", "assets", "favicon.ico", "robots.txt"
    };

    /// <summary>
    /// jobs.lever.co/{site}/{postingId}, jobs.eu.lever.co/{site}, api.lever.co/v0/postings/{site}.
    /// </summary>
    public static string? ExtractFromUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;

        if (!uri.Host.Contains("lever.co", StringComparison.OrdinalIgnoreCase))
            return null;

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return null;

        var postingsIndex = Array.FindIndex(segments, s => s.Equals("postings", StringComparison.OrdinalIgnoreCase));
        var slug = postingsIndex >= 0 && postingsIndex + 1 < segments.Length
            ? segments[postingsIndex + 1]
            : segments[0];

        if (string.IsNullOrWhiteSpace(slug) || Reserved.Contains(slug))
            return null;

        return slug.ToLowerInvariant();
    }
}
