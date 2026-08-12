using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Sources.Workday.Models;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Sources.Workday.Infrastructure;

/// <summary>
/// A Workday board is added by url. Guessing is not attempted: the address needs a host, a tenant and a site, and a
/// company name predicts none of them - not the cluster the tenant lives on, and not which of its sites is public.
/// </summary>
public sealed class WorkdayBoardResolver(
    WorkdayCxsClient cxs,
    WorkdayCareersSiteClient careersSite,
    ILog log) : IBoardResolver
{
    private readonly ILog ctxLog = log.ForContext<WorkdayBoardResolver>();

    public Task<IReadOnlyList<BoardCandidate>> ResolveByNameAsync(string companyName, CancellationToken ct)
    {
        ctxLog.Debug(
            "Workday boards are not resolvable by name ('{Company}') — a careers page url is required",
            companyName);

        return Task.FromResult<IReadOnlyList<BoardCandidate>>([]);
    }

    /// <summary>
    /// The board url, its locale-prefixed form and a link to a single vacancy all normalize onto the same board.
    /// Returns null for any url that is not a Workday one - every resolver is asked about every url.
    /// </summary>
    public async Task<BoardCandidate?> ResolveByUrlAsync(string url, CancellationToken ct)
    {
        var parts = WorkdayBoardUrl.Parse(url);
        if (parts is null)
            return null;

        // The careers page states the tenant the frontend itself uses; the host only ever suggests one.
        var confirmed = await careersSite.GetSitePairAsync(parts.BoardUrl, ct);

        if (confirmed.Success)
        {
            var pair = confirmed.Value!;

            var candidate = await ProbeConfigAsync(
                new WorkdayBoardConfig
                {
                    Host = parts.Host,
                    Tenant = pair.Tenant,
                    Site = pair.Site,
                    Kind = parts.Kind
                },
                ct);

            if (candidate is not null)
            {
                return candidate with
                {
                    Resolution = parts.IsBoardRoot ? ResolutionKind.DirectSlug : ResolutionKind.CareersPage
                };
            }
        }

        if (confirmed.NotFound)
        {
            ctxLog.Debug("Careers site {Url} does not exist — no Workday board there", parts.BoardUrl);
            return null;
        }

        // The page could not confirm the pair (an unknown tenant answers 500, and so does a real outage). The hint
        // from the url is all that is left, and the backend decides whether it was right.
        if (string.IsNullOrWhiteSpace(parts.TenantHint))
            return null;

        ctxLog.Debug(
            "Careers site {Url} did not confirm the tenant ({Error}) — falling back to the hint '{Tenant}'",
            parts.BoardUrl, confirmed.Error, parts.TenantHint);

        var fallback = await ProbeConfigAsync(
            new WorkdayBoardConfig
            {
                Host = parts.Host,
                Tenant = parts.TenantHint,
                Site = parts.Site,
                Kind = parts.Kind
            },
            ct);

        return fallback is null
            ? null
            : fallback with { Resolution = ResolutionKind.Guessed };
    }

    /// <summary>
    /// <paramref name="boardId"/> is the canonical '{host}/{tenant}/{site}'. The tenant in it may be a guess - a board
    /// token mined from a crawl index carries the subdomain, which is usually the tenant but not always - so a rejected
    /// pair is confirmed against the careers page and probed again. The candidate then carries the *confirmed* board
    /// id, which is what reaches the registry.
    /// </summary>
    public async Task<BoardCandidate?> ProbeAsync(string boardId, CancellationToken ct)
    {
        var config = WorkdayBoardConfig.FromBoardId(boardId);
        if (config is null)
        {
            ctxLog.Debug("'{Board}' is not a Workday board id — expected '{{host}}/{{tenant}}/{{site}}'", boardId);
            return null;
        }

        if (await ProbeConfigAsync(config, ct) is { } candidate)
            return candidate;

        var confirmed = await careersSite.GetSitePairAsync(config.BoardUrl, ct);
        if (!confirmed.Success)
            return null;

        var pair = confirmed.Value!;

        // The pair the page reports is the one the frontend itself calls the backend with. Re-probing it is only worth
        // a request when it differs from what has just been refused.
        if (string.Equals(pair.Tenant, config.Tenant, StringComparison.OrdinalIgnoreCase)
            && string.Equals(pair.Site, config.Site, StringComparison.Ordinal))
        {
            return null;
        }

        ctxLog.Debug(
            "Workday board {Board} was refused; the careers page reports '{Tenant}/{Site}' — probing that instead",
            config.BoardId, pair.Tenant, pair.Site);

        return await ProbeConfigAsync(
            config with
            {
                Tenant = pair.Tenant,
                Site = pair.Site
            },
            ct);
    }

    private async Task<BoardCandidate?> ProbeConfigAsync(WorkdayBoardConfig config, CancellationToken ct)
    {
        var page = await cxs.GetJobsAsync(config, offset: 0, limit: WorkdayCxsClient.MaxPageSize, ct);

        if (!page.Success)
        {
            ctxLog.Debug(
                "Workday board {Board} did not answer: {Error}",
                config.BoardId, page.Error ?? "board is missing");

            return null;
        }

        var payload = page.Value!;

        return new BoardCandidate
        {
            SourceId = WorkdayMapper.SourceId,
            BoardId = config.BoardId,
            // The tenant reads better than the canonical id and is what the company is called inside Workday.
            DisplayName = config.Tenant,
            Configuration = config.ToJson(),
            JobCount = payload.Total ?? payload.JobPostings?.Count ?? 0,
            BoardUrl = config.BoardUrl
        };
    }
}
