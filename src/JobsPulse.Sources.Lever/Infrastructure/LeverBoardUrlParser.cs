using JobsPulse.Core.Abstractions;

namespace JobsPulse.Sources.Lever.Infrastructure;

public sealed class LeverBoardUrlParser : IBoardUrlParser
{
    public string SourceId => LeverMapper.SourceId;

    public IReadOnlyList<string> IndexUrlPatterns =>
    [
        "jobs.lever.co/*",
        "jobs.eu.lever.co/*",
        "api.lever.co/v0/postings/*"
    ];

    public bool TryParseBoardId(string url, out string boardId)
    {
        boardId = string.Empty;

        var slug = LeverSiteSlug.ExtractFromUrl(url);
        if (slug is null)
            return false;

        if (slug.Length is < 2 or > 64 || !slug.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            return false;

        boardId = slug;
        return true;
    }
}
