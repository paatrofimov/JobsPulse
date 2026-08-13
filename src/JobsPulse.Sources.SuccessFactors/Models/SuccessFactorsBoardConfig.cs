using System.Text.Json;
using System.Text.Json.Serialization;
using JobsPulse.Core.Helpers;

namespace JobsPulse.Sources.SuccessFactors.Models;

/// <summary>
/// The address of a SuccessFactors career site. Like Workday, a single slug cannot address it, and for a different
/// reason: the public site of a tenant lives on the company's own domain, which the tenant id does not contain and
/// which no naming rule predicts. So the board carries all three identifiers - the branded domain that is polled, and
/// the tenant plus its data center host, which are what a crawled url and the apply flow reveal.
///
/// Only <see cref="Domain"/> is needed to poll a Career Site Builder board; the rest is identity and provenance, so a
/// configuration that never learned the tenant still works. The domain alone is also the whole address on purpose: a
/// site mounted under a path ('jobs.aldi-sued.de/Karriere') still serves every one of its routes - the feed, the
/// sitemap, the job list - from the domain root as well, so keeping the path would only invent a second board id for
/// one board.
/// </summary>
public sealed record SuccessFactorsBoardConfig
{
    /// <summary>Branded career site domain - 'jobs.sap.com'. Null for a legacy-only tenant.</summary>
    public string? Domain { get; init; }

    /// <summary>The tenant as SuccessFactors knows it - the 'company' parameter of every legacy url.</summary>
    public string? Tenant { get; init; }

    /// <summary>Data center host serving the tenant's recruiting backend - 'career5.successfactors.eu'.</summary>
    public string? RcmHost { get; init; }

    public SuccessFactorsSiteVariant Variant { get; init; } = SuccessFactorsSiteVariant.CareerSiteBuilder;

    /// <summary>Locale the site publishes in, as its own feed reports it ('en_US'). Informational.</summary>
    public string? Locale { get; init; }

    /// <summary>
    /// The board identity inside the source - derived, never parsed by client code. The branded domain when there is
    /// one, because that is what is polled and what reads well in a log; '{rcmHost}/{tenant}' otherwise.
    /// </summary>
    [JsonIgnore] public string BoardId => HasDomain ? Domain! : $"{RcmHost}/{Tenant}";

    [JsonIgnore] public bool HasDomain => !string.IsNullOrWhiteSpace(Domain);

    /// <summary>Root of the public career site, without a trailing slash.</summary>
    [JsonIgnore] public string SiteUrl => $"https://{Domain}";

    /// <summary>Where a candidate lands - the job search page of the site.</summary>
    [JsonIgnore] public string BoardUrl => HasDomain ? $"{SiteUrl}/search/" : LegacyPortalUrl;

    /// <summary>The legacy career portal of the tenant. Also the page that reveals the branded domain.</summary>
    [JsonIgnore] public string LegacyPortalUrl => $"https://{RcmHost}/career?company={Tenant}";

    /// <summary>
    /// The whole board as one rss document. Any path the site does not recognize as one of its own pages is routed
    /// to the feed servlet, which is why the file name is a configuration value rather than a discovered route.
    /// </summary>
    public string FeedUrl(string feedPath) => $"{SiteUrl}/{feedPath.TrimStart('/')}";

    /// <summary>The seo sitemap - either a job url list or the feed again, so its root element has to be sniffed.</summary>
    [JsonIgnore] public string SitemapUrl => $"{SiteUrl}/sitemap.xml";

    /// <summary>The job list fragment the search page loads by ajax, 25 tiles per request.</summary>
    public string TileSearchUrl(int startRow) =>
        $"{SiteUrl}/tile-search-results/?q=&sortColumn=referencedate&sortDirection=desc&startrow={Math.Max(0, startRow)}";

    public string ToJson() => JsonSerializer.Serialize(this, JsonSerializerOptionsFactory.Instance);

    /// <summary>Returns null on anything that is not a readable configuration, including a null input.</summary>
    public static SuccessFactorsBoardConfig? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            var config = JsonSerializer.Deserialize<SuccessFactorsBoardConfig>(
                json, JsonSerializerOptionsFactory.Instance);

            return config?.Normalized().OrNull();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The fallback for a board that has no stored configuration - a row written before the column existed, or one
    /// added by hand as '/board_add {watchlist} successfactors jobs.sap.com'. A '/' in the id is the legacy form
    /// '{rcmHost}/{tenant}'; anything else is a branded domain, which is the whole address on its own.
    /// </summary>
    public static SuccessFactorsBoardConfig? FromBoardId(string? boardId)
    {
        if (string.IsNullOrWhiteSpace(boardId))
            return null;

        var value = boardId.Trim().Trim('/');
        var slash = value.IndexOf('/');

        if (slash < 0)
            return new SuccessFactorsBoardConfig { Domain = value }.Normalized().OrNull();

        return new SuccessFactorsBoardConfig
        {
            RcmHost = value[..slash],
            Tenant = value[(slash + 1)..],
            Variant = SuccessFactorsSiteVariant.LegacyCareerPortal
        }.Normalized().OrNull();
    }

    /// <summary>
    /// Whether a host belongs to one of the SuccessFactors data centers rather than to a company. Kept here because
    /// url parsing, board id parsing and the crawl index parser all have to agree on it.
    ///
    /// A platform-hosted career site is never one, even though it sits under a data center domain
    /// ('ascendlearning.jobs.hr.cloud.sap'): it is a career site builder site like any branded one, and treating it as
    /// a data center host would send its urls to the legacy parse, which asks for a tenant no such url carries.
    /// </summary>
    public static bool IsRcmHost(string? host) =>
        !string.IsNullOrWhiteSpace(host) &&
        !IsHostedCareerSite(host) &&
        RcmDomains.Any(d => host.EndsWith('.' + d, StringComparison.OrdinalIgnoreCase) ||
                            host.Equals(d, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether a host is a career site the platform hosts itself, on a domain of SAP's rather than the company's -
    /// 'ace1950.jobs2web.com', 'ascendlearning.jobs.hr.cloud.sap'. The site behind it is an ordinary career site
    /// builder one, so the only thing this decides is that the host is a board and not a tenant to translate.
    /// </summary>
    public static bool IsHostedCareerSite(string? host) =>
        !string.IsNullOrWhiteSpace(host) &&
        HostedCareerDomains.Any(d => host.EndsWith('.' + d, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The domains SAP serves recruiting backends from. Several of them exist because the product was rebranded twice,
    /// because two regions are separate installations, and because some private clouds are hosted apart.
    /// </summary>
    public static readonly string[] RcmDomains =
    [
        "successfactors.com",
        "successfactors.eu",
        "successfactors.cn",
        "sapsf.com",
        "sapsf.eu",
        "sapsf.cn",
        "ns2cloud.com",
        "hr.cloud.sap"
    ];

    /// <summary>
    /// The domains SAP publishes career sites on for the tenants that never brought a domain of their own. 'jobs2web'
    /// is the platform under its acquired name, 'jobs.hr.cloud.sap' the same thing under the current one - which is why
    /// it sits under a data center domain and has to be recognized before one.
    /// </summary>
    public static readonly string[] HostedCareerDomains =
    [
        "jobs2web.com",
        "jobs.hr.cloud.sap"
    ];

    private SuccessFactorsBoardConfig Normalized() => this with
    {
        Domain = Trim(Domain)?.ToLowerInvariant(),
        RcmHost = Trim(RcmHost)?.ToLowerInvariant(),
        Tenant = Trim(Tenant),
        Locale = Trim(Locale)
    };

    private SuccessFactorsBoardConfig? OrNull() => IsComplete() ? this : null;

    /// <summary>Addressable means either a domain to poll or a tenant on a data center host.</summary>
    private bool IsComplete() =>
        HasDomain || (!string.IsNullOrWhiteSpace(RcmHost) && !string.IsNullOrWhiteSpace(Tenant));

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
