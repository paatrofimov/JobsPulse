using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Sources.SuccessFactors.Models;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Sources.SuccessFactors.Infrastructure;

/// <summary>
/// How a SuccessFactors board is added and how a crawled token is validated.
///
/// The shape of the problem is Workday's, one step further along: a crawled url carries a tenant, and a tenant is not
/// an address. Here it is not even a hint at one - the site that publishes the jobs lives on the company's own domain,
/// and nothing in the tenant predicts it. So the probe does the translation
/// (<see cref="SuccessFactorsCareerPortalClient"/>) and returns a candidate whose board id is the domain it found,
/// not the token it was asked about. `BoardTokenSink` stores the candidate rather than the token, which is what puts
/// the board into the registry at its real address.
/// </summary>
public sealed class SuccessFactorsBoardResolver(
    SuccessFactorsSitemapClient sitemap,
    SuccessFactorsFeedClient feed,
    SuccessFactorsCareerPortalClient portal,
    ILog log) : IBoardResolver
{
    private readonly ILog ctxLog = log.ForContext<SuccessFactorsBoardResolver>();

    /// <summary>
    /// Nothing. A company name predicts neither the branded domain its career site is on nor the tenant behind it,
    /// and guessing domains would mean probing the open internet - so SuccessFactors boards are added by url.
    /// </summary>
    public Task<IReadOnlyList<BoardCandidate>> ResolveByNameAsync(string companyName, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<BoardCandidate>>([]);

    public async Task<BoardCandidate?> ResolveByUrlAsync(string url, CancellationToken ct)
    {
        var parts = SuccessFactorsBoardUrl.Parse(url);

        if (parts is null)
            return null;

        return parts.Variant == SuccessFactorsSiteVariant.LegacyCareerPortal
            ? await FromTenantAsync(parts.RcmHost!, parts.TenantHint!, ResolutionKind.CareersPage, ct)
            : await FromDomainAsync(parts.Domain!, tenant: null, rcmHost: null, ResolutionKind.CareersPage, ct);
    }

    /// <summary>
    /// The validation step of both '/board_add' and the crawl index sweep. The id may be a branded domain, which is
    /// already the address, or the '{rcmHost}/{tenant}' token a crawl produces, which is not.
    /// </summary>
    public async Task<BoardCandidate?> ProbeAsync(string boardId, CancellationToken ct)
    {
        var config = SuccessFactorsBoardConfig.FromBoardId(boardId);

        if (config is null)
            return null;

        return config.HasDomain
            ? await FromDomainAsync(config.Domain!, config.Tenant, config.RcmHost, ResolutionKind.DirectSlug, ct)
            : await FromTenantAsync(config.RcmHost!, config.Tenant!, ResolutionKind.Guessed, ct);
    }

    /// <summary>
    /// Tenant to board. The portal page of the tenant is asked for the branded domain it hands its own candidates
    /// over to; that domain is then probed like any other, and the candidate carries it as the board id.
    /// </summary>
    private async Task<BoardCandidate?> FromTenantAsync(
        string rcmHost,
        string tenant,
        ResolutionKind resolution,
        CancellationToken ct)
    {
        var identity = await portal.GetIdentityAsync(rcmHost, tenant, ct);

        if (!identity.Success)
            return null;

        var site = identity.Value!;

        if (!site.TenantExists)
            return null;

        if (!string.IsNullOrWhiteSpace(site.Domain))
            return await FromDomainAsync(site.Domain, tenant, rcmHost, resolution, ct);

        // The tenant is real but publishes on the legacy career portal only, and that portal has no job list a poller
        // can read: it renders no rows server-side and fetches them over an internal rpc of its own. Returning a
        // candidate here would add a board that fails every cycle forever, so it is refused instead - see the
        // project CLAUDE.md for what reaching this line would take to fix.
        ctxLog.Debug(
            "Tenant {Tenant} on {Host} publishes on the legacy career portal only and cannot be polled",
            tenant, rcmHost);

        return null;
    }

    /// <summary>
    /// Confirms that a domain really publishes a board. The sitemap answers it for the price of the job urls alone,
    /// which is what keeps validating a crawl of thousands of tenants affordable; only when it cannot is the feed
    /// asked, and that one also carries the name the site calls itself.
    /// </summary>
    private async Task<BoardCandidate?> FromDomainAsync(
        string domain,
        string? tenant,
        string? rcmHost,
        ResolutionKind resolution,
        CancellationToken ct)
    {
        var config = new SuccessFactorsBoardConfig
        {
            Domain = domain,
            Tenant = tenant,
            RcmHost = rcmHost,
            Variant = SuccessFactorsSiteVariant.CareerSiteBuilder
        };

        var summary = await sitemap.GetSummaryAsync(config, ct);

        var jobCount = 0;
        string? title = null;

        if (summary.Success)
        {
            jobCount = summary.Value!.JobCount;
            title = summary.Value!.Title;
        }
        else
        {
            var response = await feed.GetFeedAsync(config, includeDescriptions: false, ct);

            if (!response.Success)
                return null;

            jobCount = response.Value!.Items.Count;
            title = response.Value!.Title;
            config = config with { Locale = response.Value!.Language };
        }

        return new BoardCandidate
        {
            SourceId = SuccessFactorsMapper.SourceId,
            BoardId = config.BoardId,
            DisplayName = string.IsNullOrWhiteSpace(title) ? CompanyNameOf(domain) : title,
            Configuration = config.ToJson(),
            JobCount = jobCount,
            BoardUrl = config.BoardUrl,
            Resolution = resolution
        };
    }

    /// <summary>
    /// A readable company name out of a career domain, for the sites whose sitemap is the cheap url list and
    /// therefore carries no name of their own: 'jobs.aldi-sued.de' reads as 'aldi-sued'.
    /// </summary>
    private static string CompanyNameOf(string domain)
    {
        // On a platform-hosted site the whole domain but the tenant label belongs to SAP, so no suffix rule applies:
        // 'ascendlearning.jobs.hr.cloud.sap' reads as 'ascendlearning'.
        var hosted = SuccessFactorsBoardConfig.HostedCareerDomains
            .FirstOrDefault(d => domain.EndsWith('.' + d, StringComparison.OrdinalIgnoreCase));

        if (hosted is not null)
            return domain[..^(hosted.Length + 1)];

        var labels = domain.Split('.', StringSplitOptions.RemoveEmptyEntries);

        var meaningful = labels
            .Where(l => l is not ("www" or "jobs" or "job" or "career" or "careers" or "karriere" or "emploi"))
            .ToArray();

        // Everything but the public suffix - which is one label ('.com') or two ('.co.uk', '.com.au').
        var drop = meaningful.Length >= 3 && meaningful[^2].Length <= 3 ? 2 : 1;

        return meaningful.Length > drop
            ? string.Join('.', meaningful[..^drop])
            : domain;
    }
}
