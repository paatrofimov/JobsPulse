using System.Net;
using System.Text.RegularExpressions;

namespace JobsPulse.Core.Infrastructure;

/// <summary>A description as lines and sentences - the units the description detectors read.</summary>
public static partial class DescriptionText
{
    /// <summary>
    /// Plain text keeps its lines; HTML (also the entity-escaped one some boards return) becomes one line per block,
    /// with list items prefixed by a bullet so they are never mistaken for headings.
    /// </summary>
    public static string[] ToLines(string text)
    {
        if (text.Contains("&lt;", StringComparison.Ordinal))
            text = WebUtility.HtmlDecode(text);

        if (text.Contains('<'))
        {
            text = ListItemTag().Replace(text, "\n- ");
            text = BlockTag().Replace(text, "\n");
            text = AnyTag().Replace(text, " ");
        }

        text = WebUtility.HtmlDecode(text);

        return text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
    }

    /// <summary>
    /// Every line cut at its sentence ends. A requirement bullet is one sentence, so a rule judging «this sentence»
    /// judges one requirement and not the whole ad.
    /// </summary>
    public static IEnumerable<string> Sentences(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;

        foreach (var line in ToLines(text))
        {
            foreach (var sentence in SentenceEnd().Split(line))
            {
                if (sentence.Length > 0)
                    yield return sentence;
            }
        }
    }

    [GeneratedRegex(@"<li[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ListItemTag();

    [GeneratedRegex(@"<(br|/?p|/?div|/?h[1-6]|/?ul|/?ol|/li|/?tr|/?section)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockTag();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex AnyTag();

    // «. » / «; » / «! » followed by a capital or a digit - not «? », a form field «Sponsorship? No» is one sentence; «e.g. Java» and «.NET» stay inside their sentence.
    [GeneratedRegex(@"(?<=[a-z0-9\)#+][.!])(?<!\b(?:e\.g|i\.e|incl|approx|vs|ex)\.)\s+(?=[A-Z0-9])|(?<=;)\s+|\s+[•·▪●]\s+")]
    private static partial Regex SentenceEnd();
}
