using System.Net;
using System.Text.RegularExpressions;
using JobsPulse.Core.Infrastructure;
using JobsPulse.Sources.SuccessFactors.Models;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Sources.SuccessFactors.Infrastructure;

/// <summary>
/// Turns a tenant into the branded site that actually publishes its jobs. This is the piece that makes discovery
/// possible at all.
///
/// The problem it solves: every live career site builder instance sits on a domain of the company's own
/// ('jobs.sap.com', 'careers.swissre.com'), so there is no host pattern a crawl index can be asked for. What *can* be
/// asked for is the data center hosts, and a legacy url spells its tenant out in 'company='. The missing step is
/// tenant to domain - and the tenant's own legacy portal page has it, because the portal has to hand the candidate
/// over to the branded site for logout, the talent community and the brand links.
///
/// The portal answers HTTP 200 for a tenant it does not know as readily as for one it does, so the presence of the
/// career form is what says the tenant is real - not the status code.
/// </summary>
public sealed partial class SuccessFactorsCareerPortalClient(
    LoggingHttpClient http,
    ILog log)
{
    /// <summary>The portal builds these three from the tenant's branded domain whenever it has one.</summary>
    [GeneratedRegex(
        @"https?://(?<host>[a-z0-9][a-z0-9.-]*\.[a-z]{2,})/(?:services/security/logoutp|talentcommunity|services/cas)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BrandLink();

    [GeneratedRegex(
        @"brandUrl=(?:https?(?::|%3a)(?://|%2f%2f))(?<host>[a-z0-9][a-z0-9.-]*\.[a-z]{2,})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BrandUrl();

    /// <summary>The career form is on every real portal page and on no error page.</summary>
    [GeneratedRegex(
        @"(?:id=""careerform""|name=""career_company""|name=""career_ns"")",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CareerForm();

    /// <summary>Portal pages are large; the brand links sit in the head and the form well before the job search markup.</summary>
    private const int MaxBytes = 512 * 1024;

    private readonly ILog ctxLog = log.ForContext<SuccessFactorsCareerPortalClient>();

    public async Task<SuccessFactorsFetch<SuccessFactorsSiteIdentity>> GetIdentityAsync(
        string rcmHost,
        string tenant,
        CancellationToken ct)
    {
        var url = $"https://{rcmHost}/career?company={Uri.EscapeDataString(tenant)}";

        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return SuccessFactorsFetch<SuccessFactorsSiteIdentity>.Missing();

            if (!response.IsSuccessStatusCode)
                return SuccessFactorsFetch<SuccessFactorsSiteIdentity>.Failure($"HTTP {(int)response.StatusCode}");

            var html = await ReadAsync(response, ct);

            if (!CareerForm().IsMatch(html))
            {
                // The portal answered, but with something that is not a career page - an unknown tenant, or a page
                // asking to log in. Not a failure of ours and not proof the tenant is gone either.
                ctxLog.Debug("Portal of {Tenant} on {Host} carries no career form", tenant, rcmHost);

                return SuccessFactorsFetch<SuccessFactorsSiteIdentity>.Ok(SuccessFactorsSiteIdentity.None);
            }

            return SuccessFactorsFetch<SuccessFactorsSiteIdentity>.Ok(new SuccessFactorsSiteIdentity
            {
                TenantExists = true,
                Domain = BrandDomain(html, rcmHost)
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return SuccessFactorsFetch<SuccessFactorsSiteIdentity>.Failure(ex.Message);
        }
    }

    /// <summary>
    /// The branded domain out of the portal page. Anything on a SuccessFactors data center host is skipped: those are
    /// the portal's own links, and 'api{N}.jobs2web.com' is skipped for the same reason - it is the platform's api
    /// host, not a career site anyone can be sent to. A platform-hosted career site
    /// ('ace1950.jobs2web.com', 'x.jobs.hr.cloud.sap') is kept: for a tenant without a domain of its own that is the
    /// answer.
    /// </summary>
    private static string? BrandDomain(string html, string rcmHost)
    {
        foreach (var candidate in Candidates(html))
        {
            var host = candidate.ToLowerInvariant();

            if (host == rcmHost)
                continue;

            if (SuccessFactorsBoardConfig.IsRcmHost(host))
                continue;

            if (SuccessFactorsBoardConfig.IsHostedCareerSite(host) &&
                host.StartsWith("api", StringComparison.Ordinal))
            {
                continue;
            }

            return host;
        }

        return null;
    }

    private static IEnumerable<string> Candidates(string html)
    {
        foreach (Match m in BrandLink().Matches(html))
            yield return m.Groups["host"].Value;

        foreach (Match m in BrandUrl().Matches(html))
            yield return m.Groups["host"].Value;
    }

    private static async Task<string> ReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var body = await response.Content.ReadAsStreamAsync(ct);
        await using var budgeted = new ByteBudgetStream(body, MaxBytes);
        using var reader = new StreamReader(budgeted);

        return await reader.ReadToEndAsync(ct);
    }
}
