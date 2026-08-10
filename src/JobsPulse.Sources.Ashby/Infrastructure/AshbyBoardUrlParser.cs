using JobsPulse.Core.Abstractions;

namespace JobsPulse.Sources.Ashby.Infrastructure;

public sealed class AshbyBoardUrlParser : IBoardUrlParser
{
    public string SourceId => AshbyMapper.SourceId;

    public IReadOnlyList<string> IndexUrlPatterns =>
    [
        "jobs.ashbyhq.com/*",
        "api.ashbyhq.com/posting-api/job-board/*"
    ];

    public bool TryParseBoardId(string url, out string boardId)
    {
        boardId = string.Empty;

        var slug = AshbyJobBoardSlug.ExtractFromUrl(url);
        if (slug is null)
            return false;

        // Job board names are short single-segment tokens; anything else is a posting page or a query artefact.
        if (slug.Length is < 2 or > 64 || !slug.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
            return false;

        boardId = slug;
        return true;
    }
}
