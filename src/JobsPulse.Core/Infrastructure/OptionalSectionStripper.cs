using System.Text;
using System.Text.RegularExpressions;

namespace JobsPulse.Core.Infrastructure;

public static partial class OptionalSectionStripper
{
    private static readonly string[] OptionalMarkers =
    [
        "nice to have", "nice-to-have", "nice to haves", "good to have", "great to have",
        "bonus", "pluses", "extra credit", "would be a plus", "is a plus", "it's a plus",
        "preferred qualifications", "preferred skills", "preferred experience", "preferred requirements",
        "desired skills", "desired qualifications", "desired experience", "desired characteristics", "desirable",
        "what would make you stand out", "what will make you stand out", "to stand out", "even better if"
    ];

    // Found anywhere in a heading line: «What else we look for (Preferred):», «What we'd like to see, but not required».
    private static readonly string[] InHeadingMarkers = ["(preferred)", "not required"];

    private const string PreferredHeading = "preferred";

    private const string Bullets = "-*•·▪●◦○–—✔✓";

    private const int MaxHeadingLength = 60;
    private const int MaxInlineHeadingLength = 30;

    public static string? Strip(string? description)
    {
        if (string.IsNullOrEmpty(description) || !ContainsMarker(description))
            return description;

        var lines = DescriptionText.ToLines(description);
        var kept = new StringBuilder(description.Length);
        var skipping = false;
        var stripped = false;

        foreach (var line in lines)
        {
            if (IsOptionalHeading(line))
            {
                skipping = true;
                stripped = true;
                continue;
            }

            if (skipping && IsHeading(line))
                skipping = false;

            if (!skipping)
                kept.Append(line).Append('\n');
        }

        // Without an optional section the text is matched as it came, so nothing else changes for it.
        return stripped ? kept.ToString() : description;
    }

    private static bool ContainsMarker(string text) =>
        OptionalMarkers.Concat(InHeadingMarkers).Append(PreferredHeading)
            .Any(m => text.Contains(m, StringComparison.OrdinalIgnoreCase));

    /// <summary>A short line that is not a list item and does not end like a sentence.</summary>
    private static bool IsHeading(string line)
    {
        if (line.Length > MaxHeadingLength || Bullets.Contains(line[0]))
            return false;

        if (!char.IsLetter(LeadingSymbols().Replace(line, "").FirstOrDefault()))
            return false;

        return line[^1] is not ('.' or ',' or ';');
    }

    /// <summary>
    /// A heading that starts with an optional marker, or a line like «Nice to have: C#, .NET» whose marker part is a
    /// heading in itself. A long sentence that merely mentions a marker is not one.
    /// </summary>
    private static bool IsOptionalHeading(string line)
    {
        var text = LeadingSymbols().Replace(line, "");

        if (IsHeading(line) &&
            (InHeadingMarkers.Any(m => text.Contains(m, StringComparison.OrdinalIgnoreCase)) ||
             text.TrimEnd(':', ' ').Equals(PreferredHeading, StringComparison.OrdinalIgnoreCase)))
            return true;

        var marker = OptionalMarkers.FirstOrDefault(m => text.StartsWith(m, StringComparison.OrdinalIgnoreCase));
        if (marker is null)
            return false;

        if (IsHeading(line))
            return true;

        var colon = text.IndexOf(':');

        return colon >= marker.Length && colon <= MaxInlineHeadingLength;
    }

    [GeneratedRegex(@"^[^\p{L}\p{N}]+")]
    private static partial Regex LeadingSymbols();
}
