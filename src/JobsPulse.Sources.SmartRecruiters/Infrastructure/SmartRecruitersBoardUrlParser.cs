using JobsPulse.Core.Abstractions;

namespace JobsPulse.Sources.SmartRecruiters.Infrastructure;

public sealed class SmartRecruitersBoardUrlParser : IBoardUrlParser
{
    public string SourceId => SmartRecruitersMapper.SourceId;

    public IReadOnlyList<string> IndexUrlPatterns =>
    [
        "jobs.smartrecruiters.com/*",
        "careers.smartrecruiters.com/*",
        "api.smartrecruiters.com/v1/companies/*"
    ];

    public bool TryParseBoardId(string url, out string boardId)
    {
        boardId = string.Empty;

        var slug = SmartRecruitersCompanySlug.ExtractFromUrl(url);
        if (slug is null)
            return false;

        // Company identifiers are short single-segment tokens; anything else is a job page or a query artefact.
        if (slug.Length is < 2 or > 64 || !slug.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            return false;

        boardId = slug;
        return true;
    }
}
