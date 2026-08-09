using JobsPulse.Core.Abstractions;

namespace JobsPulse.Sources.Greenhouse.Infrastructure;

public sealed class GreenhouseBoardUrlParser : IBoardUrlParser
{
    // Slugs that are part of the Greenhouse url scheme itself, not company boards.
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "embed", "v1", "api", "boards", "auth", "static", "assets", "favicon.ico", "robots.txt", "jobs"
    };

    public string SourceId => GreenhouseMapper.SourceId;

    public IReadOnlyList<string> IndexUrlPatterns =>
    [
        "boards.greenhouse.io/*",
        "job-boards.greenhouse.io/*",
        "boards-api.greenhouse.io/v1/boards/*"
    ];

    public bool TryParseBoardId(string url, out string boardId)
    {
        boardId = string.Empty;

        var slug = SlugGuesser.ExtractFromUrl(url);
        if (slug is null || Reserved.Contains(slug))
            return false;

        // Board tokens are short lowercase identifiers; anything else is a job page or a query artefact.
        if (slug.Length is < 2 or > 64 || !slug.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            return false;

        boardId = slug;
        return true;
    }
}
