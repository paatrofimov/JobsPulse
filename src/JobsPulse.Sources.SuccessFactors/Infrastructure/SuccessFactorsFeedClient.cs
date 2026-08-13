using System.Net;
using System.Xml;
using JobsPulse.Core.Infrastructure;
using JobsPulse.Sources.SuccessFactors.Models;
using JobsPulse.Sources.SuccessFactors.Options;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Sources.SuccessFactors.Infrastructure;

/// <summary>
/// The one request a Career Site Builder board costs: the whole board as one rss document, descriptions included.
/// Every path a career site does not recognize as one of its own pages is routed to the feed servlet, which is why the
/// feed is asked for under a name of our choosing (<see cref="SuccessFactorsOptions.FeedPath"/>) rather than a route
/// the site publishes - and why '/sitemap.xml' must not be that name: some sites do recognize it, and answer with the
/// seo url list instead.
///
/// The feed takes no parameters. Page size, locale, a request to leave the descriptions out - all of them are ignored,
/// so the byte budget is the only lever there is over what a large board costs.
/// </summary>
public sealed class SuccessFactorsFeedClient(
    LoggingHttpClient http,
    IOptionsMonitor<SuccessFactorsOptions> options,
    ILog log)
{
    public const string HttpClientName = "successfactors";

    private readonly ILog ctxLog = log.ForContext<SuccessFactorsFeedClient>();

    public async Task<SuccessFactorsFetch<JobFeedDto>> GetFeedAsync(
        SuccessFactorsBoardConfig config,
        bool includeDescriptions,
        CancellationToken ct)
    {
        var opts = options.CurrentValue;
        var url = config.FeedUrl(opts.FeedPath);

        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return SuccessFactorsFetch<JobFeedDto>.Missing();

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(30);
                ctxLog.Warn("Career site is throttling 429: {Url}, asked to wait for: {Delay}", url, retryAfter);

                return SuccessFactorsFetch<JobFeedDto>.Failure(
                    $"rate limited, retry after {retryAfter.TotalSeconds:F0}s");
            }

            // A branded domain is the company's own, so it can sit behind their web application firewall. That is a
            // refusal to answer, never a board that stopped existing.
            if (!response.IsSuccessStatusCode)
                return SuccessFactorsFetch<JobFeedDto>.Failure($"HTTP {(int)response.StatusCode}");

            await using var body = await response.Content.ReadAsStreamAsync(ct);
            await using var budgeted = new ByteBudgetStream(body, Math.Max(1024, opts.MaxFeedBytes));

            try
            {
                var feed = await SuccessFactorsFeedParser.ParseAsync(budgeted, includeDescriptions, ct);

                return SuccessFactorsFetch<JobFeedDto>.Ok(feed);
            }
            catch (XmlException ex)
            {
                // The budget ran out mid-document, or the site cut the response off itself. Either way what was read
                // is a part of the board, and committing a part of a board closes everything that did not fit.
                if (budgeted.BudgetExceeded)
                {
                    return SuccessFactorsFetch<JobFeedDto>.TooLarge(
                        $"feed exceeds the {opts.MaxFeedBytes} byte budget");
                }

                ctxLog.Warn(ex, "Career site cut the feed of {Url} off mid-document", url);

                return SuccessFactorsFetch<JobFeedDto>.TooLarge($"feed is truncated: {ex.Message}");
            }
            catch (InvalidDataException ex)
            {
                // Well formed, but not a feed: an error page answered with HTTP 200, or a site that does recognize
                // the requested name and answers something else under it. Nothing to fall back to and nothing to
                // retry, so this is a plain failure - and never a missing board.
                ctxLog.Warn("Career site answered {Url} with something that is not a job feed: {Error}", url, ex.Message);

                return SuccessFactorsFetch<JobFeedDto>.Failure($"not a job feed: {ex.Message}");
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return SuccessFactorsFetch<JobFeedDto>.Failure(ex.Message);
        }
    }
}
