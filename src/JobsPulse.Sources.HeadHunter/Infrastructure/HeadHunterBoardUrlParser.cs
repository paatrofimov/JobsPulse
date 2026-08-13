using JobsPulse.Core.Abstractions;

namespace JobsPulse.Sources.HeadHunter.Infrastructure;

/// <summary>
/// Crawl index mining for HeadHunter. The catalog is one set of hosts rather than a host per tenant, so the patterns
/// are the employer pages of every regional site - and every city subdomain of one ('spb.hh.ru/employer/1740') is an
/// employer page too, which is what the whole-domain mode is for.
///
/// Only an employer url yields a token. A crawled *vacancy* page names the employer nowhere in its url, so turning one
/// into a board would cost a request per crawled url - the discovery pipeline is deliberately pure here, and the
/// employer pages are plentiful enough that nothing is lost.
/// </summary>
public sealed class HeadHunterBoardUrlParser : IBoardUrlParser
{
    public string SourceId => HeadHunterMapper.SourceId;

    public IReadOnlyList<string> IndexUrlPatterns { get; } =
    [
        .. HeadHunterUrl.Domains.Select(d => $"{d}/employer/*"),
        .. HeadHunterUrl.Domains.Select(d => $"*.{d}/employer/*")
    ];

    public bool TryParseBoardId(string url, out string boardId)
    {
        boardId = string.Empty;

        var parts = HeadHunterUrl.Parse(url);

        if (parts?.EmployerId is not { } employerId)
            return false;

        // The employer id is the whole board address, so what the crawl found is already what the registry stores -
        // the probe only has to confirm the employer exists.
        boardId = employerId;

        return true;
    }
}
