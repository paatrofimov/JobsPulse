using System.Net;
using System.Text.RegularExpressions;
using JobsPulse.Sources.SuccessFactors.Models;

namespace JobsPulse.Sources.SuccessFactors.Infrastructure;

/// <summary>
/// Reads the job tiles out of the html fragment the search page loads by ajax.
///
/// It is deliberately anchored on the two things the platform writes itself - the 'job-id-{id}' class and the
/// 'data-url' attribute of the tile element - and treats everything else as optional. The contents of a tile are
/// configured per customer in Career Site Builder, so a location cell is present on some sites and absent on others,
/// and no layout can be assumed. This is why the feed is the primary strategy and this one is the fallback: here a
/// missing field is normal, there it is not.
/// </summary>
public static partial class SuccessFactorsTileParser
{
    [GeneratedRegex(
        @"<li[^>]*\bclass=""[^""]*\bjob-tile\b[^""]*""[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TileStart();

    [GeneratedRegex(@"\bjob-id-(\d+)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TileId();

    [GeneratedRegex(@"\bdata-url=""([^""]+)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TileUrl();

    [GeneratedRegex(
        @"<a[^>]*\bclass=""[^""]*\bjobTitle-link\b[^""]*""[^>]*>(?<text>.*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex TitleLink();

    [GeneratedRegex(
        @"data-careersite-propertyid=""location""[^>]*>(?<text>.*?)</span>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex LocationCell();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex Tags();

    public static IReadOnlyList<JobTileDto> Parse(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return [];

        var starts = TileStart().Matches(html);
        var tiles = new List<JobTileDto>(starts.Count);

        for (var i = 0; i < starts.Count; i++)
        {
            // One tile is everything up to where the next one begins - the markup nests, so counting '</li>' would
            // need a parser and buys nothing: the fields are matched by their own markers anyway.
            var start = starts[i].Index;
            var end = i + 1 < starts.Count ? starts[i + 1].Index : html.Length;
            var tile = html[start..end];

            var id = TileId().Match(tile);
            var url = TileUrl().Match(tile);

            if (!id.Success || !url.Success)
                continue;

            tiles.Add(new JobTileDto
            {
                Id = id.Groups[1].Value,
                Url = WebUtility.HtmlDecode(url.Groups[1].Value),
                Title = Text(TitleLink().Match(tile)),
                Location = Text(LocationCell().Match(tile))
            });
        }

        return tiles;
    }

    private static string? Text(Match match)
    {
        if (!match.Success)
            return null;

        var text = WebUtility.HtmlDecode(Tags().Replace(match.Groups["text"].Value, " "));

        text = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
