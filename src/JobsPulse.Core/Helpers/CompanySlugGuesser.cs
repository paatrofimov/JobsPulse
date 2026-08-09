using System.Globalization;
using System.Text;

namespace JobsPulse.Core.Helpers;

/// <summary>Company name to ATS slug candidates - the same guessing rules for every source.</summary>
public static class CompanySlugGuesser
{
    private static readonly string[] Suffixes =
        ["-inc", "-llc", "-ltd", "-gmbh", "-bv", "-corp", "-co", "-group", "-labs", "-technologies", "-tech"];

    public static IReadOnlyList<string> Generate(string companyName, int max)
    {
        var normalized = Normalize(companyName);
        if (normalized.Length == 0)
            return [];

        var compact = normalized.Replace("-", string.Empty);

        var candidates = new List<string>
        {
            normalized,              // "acme-corp"
            compact,                 // "acmecorp"
            StripSuffix(normalized), // "acme" (inc/ltd/gmbh/... dropped)
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

    private static string Normalize(string name)
    {
        var decomposed = name.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);

        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(ch))
                sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-')
                sb.Append('-');
        }

        return sb.ToString().Trim('-');
    }

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
