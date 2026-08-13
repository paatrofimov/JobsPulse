using JobsPulse.Core.Abstractions;
using JobsPulse.Sources.SuccessFactors.Models;

namespace JobsPulse.Sources.SuccessFactors.Infrastructure;

/// <summary>
/// Crawl index mining for SuccessFactors, and the one source whose token is mined from the *query string*.
///
/// The reason is that the thing a crawl can find and the thing that can be polled are different hosts. Career site
/// builder sites - which is what every live instance is - sit on the company's own domain, so there is no host
/// pattern to ask an index for. What is enumerable is the handful of data center hosts SAP runs, and a legacy url on
/// one of them spells its tenant out in 'company=': 'career8.successfactors.com/career?company=brevardcou'. Those
/// urls are everywhere - apply links, old postings, aggregator copies - so the crawl yields tenants, and the probe
/// turns a tenant into the branded domain that is actually polled (<see cref="SuccessFactorsBoardResolver"/>).
///
/// The patterns are whole domains rather than known hosts, the same mode Workday needs, because the data center host
/// carries a number that is per tenant. They also declare a query key, which is what makes the columnar index project
/// the 'company' parameter instead of dropping the query - see `BoardIndexTarget.QueryKeys`.
/// </summary>
public sealed class SuccessFactorsBoardUrlParser : IBoardUrlParser
{
    public string SourceId => SuccessFactorsMapper.SourceId;

    public IReadOnlyList<string> IndexUrlPatterns { get; } =
    [
        // Every data center domain, asked about the career portal path and the one query parameter that matters.
        .. SuccessFactorsBoardConfig.RcmDomains.Select(d => $"*.{d}/career?company=*"),

        // The platform's own hosted sites, the only career sites that are not on a company domain.
        "*.jobs2web.com/*"
    ];

    public bool TryParseBoardId(string url, out string boardId)
    {
        boardId = string.Empty;

        var parts = SuccessFactorsBoardUrl.Parse(url);

        if (parts is null)
            return false;

        // A jobs2web url is already a site - there is nothing to translate, the domain is the board.
        if (parts.Variant == SuccessFactorsSiteVariant.CareerSiteBuilder)
        {
            if (string.IsNullOrWhiteSpace(parts.Domain))
                return false;

            boardId = parts.Domain;

            return true;
        }

        if (string.IsNullOrWhiteSpace(parts.RcmHost) || string.IsNullOrWhiteSpace(parts.TenantHint))
            return false;

        // The token is the tenant on its data center host. It is not the address the board will be stored under -
        // the probe replaces it with the branded domain it resolves to.
        boardId = $"{parts.RcmHost}/{parts.TenantHint}";

        return true;
    }
}
