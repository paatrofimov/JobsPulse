namespace JobsPulse.Sources.Ashby.Infrastructure;

public static class AshbyJobBoardSlug
{
    // Path segments of the Ashby url scheme itself, never a job board name.
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "posting-api", "job-board", "api", "embed", "application", "assets", "static",
        "favicon.ico", "robots.txt", "sitemap.xml"
    };

    /// <summary>
    /// jobs.ashbyhq.com/{board}/{postingId}, api.ashbyhq.com/posting-api/job-board/{board}.
    /// </summary>
    public static string? ExtractFromUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;

        if (!uri.Host.EndsWith("ashbyhq.com", StringComparison.OrdinalIgnoreCase))
            return null;

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return null;

        var apiIndex = Array.FindIndex(segments, s => s.Equals("job-board", StringComparison.OrdinalIgnoreCase));
        var slug = apiIndex >= 0 && apiIndex + 1 < segments.Length
            ? segments[apiIndex + 1]
            : segments[0];

        if (string.IsNullOrWhiteSpace(slug) || Reserved.Contains(slug))
            return null;

        // Board lookup is case-insensitive, so the lowercase form is stored - one board, one registry row.
        return slug.ToLowerInvariant();
    }
}
