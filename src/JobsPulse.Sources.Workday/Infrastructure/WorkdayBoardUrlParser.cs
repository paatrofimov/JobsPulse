using System.Text.RegularExpressions;
using JobsPulse.Core.Abstractions;
using JobsPulse.Sources.Workday.Models;

namespace JobsPulse.Sources.Workday.Infrastructure;

/// <summary>
/// `IBoardUrlParser` for crawl index mining. Workday is the one ATS whose board host is per tenant, so the patterns
/// name whole domains instead of a known host, and the token this produces carries a tenant the crawl could not
/// confirm - the subdomain for a `myworkdayjobs.com` url, the real one for a `myworkdaysite.com` one.
///
/// That is fine because the last stage of discovery probes every token against the ATS itself:
/// <see cref="WorkdayBoardResolver.ProbeAsync"/> confirms the pair against the careers page when the guessed one is
/// rejected, and a token nothing answers for is dropped instead of reaching the registry.
/// </summary>
public sealed partial class WorkdayBoardUrlParser : IBoardUrlParser
{
    public string SourceId => WorkdayMapper.SourceId;

    public IReadOnlyList<string> IndexUrlPatterns =>
    [
        "*.myworkdayjobs.com/*",
        "*.myworkdaysite.com/recruiting/*"
    ];

    public bool TryParseBoardId(string url, out string boardId)
    {
        boardId = string.Empty;

        var parts = WorkdayBoardUrl.Parse(url);
        if (parts?.TenantHint is not { Length: > 0 } tenant)
            return false;

        // '{tenant}.{cluster}.myworkdayjobs.com' - a url of the cluster host itself has no tenant label to read.
        if (parts.Kind == WorkdayHostKind.MyWorkdayJobs
            && (parts.Host.Split('.').Length < 4 || ClusterLabel().IsMatch(tenant)))
        {
            return false;
        }

        boardId = new WorkdayBoardConfig
        {
            Host = parts.Host,
            Tenant = tenant,
            Site = parts.Site,
            Kind = parts.Kind
        }.BoardId;

        return true;
    }

    /// <summary>'wd1', 'wd103' - the Workday cluster, never a company.</summary>
    [GeneratedRegex("^wd[0-9]{1,3}$", RegexOptions.IgnoreCase)]
    private static partial Regex ClusterLabel();
}
