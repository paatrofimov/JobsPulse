using System.Net;
using System.Text.RegularExpressions;
using JobsPulse.Core.Infrastructure;
using JobsPulse.Sources.Workday.Models;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Sources.Workday.Infrastructure;

/// <summary>
/// Reads the tenant and the site out of the careers page itself. The page bootstraps its frontend with
/// `window.workday = { tenant: "...", siteId: "..." }`, which is the same pair the frontend then calls the backend
/// with - so it is the authoritative answer rather than a guess from the host name.
/// </summary>
public sealed partial class WorkdayCareersSiteClient(
    LoggingHttpClient http,
    ILog log)
{
    public const string HttpClientName = "workday";

    private readonly ILog ctxLog = log.ForContext<WorkdayCareersSiteClient>();

    /// <summary>
    /// A missing site answers 404, an unknown tenant or host answers 500 - and a real outage answers 500 too, which
    /// is why only the 404 is reported as <see cref="WorkdayFetch{T}.Missing"/> and the rest is a plain failure.
    /// </summary>
    public async Task<WorkdayFetch<WorkdaySitePair>> GetSitePairAsync(string boardUrl, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(boardUrl, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return WorkdayFetch<WorkdaySitePair>.Missing();

            if (!response.IsSuccessStatusCode)
                return WorkdayFetch<WorkdaySitePair>.Failure($"HTTP {(int)response.StatusCode}");

            var html = await response.Content.ReadAsStringAsync(ct);

            var tenant = BootstrapValue(TenantPattern(), html);
            var site = BootstrapValue(SiteIdPattern(), html);

            if (tenant is null || site is null)
            {
                ctxLog.Debug("Careers page {Url} carries no workday bootstrap — tenant is not confirmed", boardUrl);
                return WorkdayFetch<WorkdaySitePair>.Failure("no workday bootstrap on the page");
            }

            return WorkdayFetch<WorkdaySitePair>.Ok(new WorkdaySitePair
            {
                Tenant = tenant,
                Site = site
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return WorkdayFetch<WorkdaySitePair>.Failure(ex.Message);
        }
    }

    private static string? BootstrapValue(Regex pattern, string html)
    {
        var value = pattern.Match(html).Groups["value"].Value;

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    [GeneratedRegex(@"tenant\s*:\s*""(?<value>[^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex TenantPattern();

    [GeneratedRegex(@"siteId\s*:\s*""(?<value>[^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex SiteIdPattern();
}
