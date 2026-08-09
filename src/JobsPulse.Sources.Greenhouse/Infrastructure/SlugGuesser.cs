using JobsPulse.Core.Helpers;

namespace JobsPulse.Sources.Greenhouse.Infrastructure;

public static class SlugGuesser
{
    public static IReadOnlyList<string> Generate(string companyName, int max) =>
        CompanySlugGuesser.Generate(companyName, max);

    public static string? ExtractFromUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;

        var host = uri.Host.ToLowerInvariant();
        var isGreenhouse = host.Contains("greenhouse.io", StringComparison.Ordinal);
        if (!isGreenhouse) return null;

        // boards.greenhouse.io/{slug}, job-boards.greenhouse.io/{slug},
        // boards-api.greenhouse.io/v1/boards/{slug}/...
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return null;

        var boardsIndex = Array.FindIndex(segments, s => s.Equals("boards", StringComparison.OrdinalIgnoreCase));
        var slug = boardsIndex >= 0 && boardsIndex + 1 < segments.Length
            ? segments[boardsIndex + 1]
            : segments[0];

        return string.IsNullOrWhiteSpace(slug) || slug is "v1" or "embed" ? null : slug.ToLowerInvariant();
    }
}
