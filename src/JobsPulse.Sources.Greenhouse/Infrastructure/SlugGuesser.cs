using System.Globalization;
using System.Text;

namespace JobsPulse.Sources.Greenhouse.Infrastructure;

public static class SlugGuesser
{
    public static IReadOnlyList<string> Generate(string companyName, int max)
    {
        var normalized = Normalize(companyName);
        if (normalized.Length == 0) return [];

        var compact = normalized.Replace("-", string.Empty);

        var candidates = new List<string>
        {
            normalized,                    // "acme-corp"
            compact,                       // "acmecorp"
            StripSuffix(normalized),       // "acme"   (убрали inc/ltd/gmbh/...)
            StripSuffix(compact),
            compact + "hq",
            "get" + compact,
            compact + "ai",
            compact + "io"
        };

        return
        [
            .. candidates
                .Where(c => c.Length >= 2)
                .Distinct(StringComparer.Ordinal)
                .Take(max)
        ];
    }

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

    private static string Normalize(string name)
    {
        var decomposed = name.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);

        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;

            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }

        return sb.ToString().Trim('-');
    }

    private static readonly string[] Suffixes =
        ["-inc", "-llc", "-ltd", "-gmbh", "-bv", "-corp", "-co", "-group", "-labs", "-technologies", "-tech"];

    private static string StripSuffix(string slug)
    {
        foreach (var suffix in Suffixes)
        {
            if (slug.EndsWith(suffix, StringComparison.Ordinal))
                return slug[..^suffix.Length];

            var compact = suffix[1..];
            if (slug.Length > compact.Length + 2 && slug.EndsWith(compact, StringComparison.Ordinal))
                return slug[..^compact.Length];
        }

        return slug;
    }
}