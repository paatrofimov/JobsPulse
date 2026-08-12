using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using JobsPulse.Core.Infrastructure;
using JobsPulse.Discovery.Abstractions;
using JobsPulse.Discovery.Models;
using JobsPulse.Discovery.Options;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Discovery.Infrastructure;

public sealed partial class CrawlIndexClient(
    LoggingHttpClient http,
    IOptionsMonitor<DiscoveryOptions> options,
    ILog log) : ICrawlIndexClient
{
    public const string HttpClientName = "common-crawl-index";
    private const string CollectionsPath = "collinfo.json";

    private readonly ILog ctxLog = log.ForContext<CrawlIndexClient>();

    // Every outgoing request goes through this gate, so pacing and the throttle penalty are single-threaded.
    private readonly SemaphoreSlim requestGate = new(1, 1);
    private readonly Stopwatch uptime = Stopwatch.StartNew();

    private TimeSpan lastRequestAt = TimeSpan.MinValue;
    private TimeSpan throttlePenalty = TimeSpan.Zero;
    private int successesSincePenalty;

    private IReadOnlyList<CrawlCollection>? cachedCollections;
    private TimeSpan cachedCollectionsAt;

    public async Task<IReadOnlyList<CrawlCollection>> GetCollectionsAsync(CancellationToken ct)
    {
        var opts = options.CurrentValue;
        var ttl = TimeSpan.FromMinutes(Math.Max(0, opts.CollectionsCacheMinutes));

        if (cachedCollections is { Count: > 0 } cached && uptime.Elapsed - cachedCollectionsAt < ttl)
        {
            ctxLog.Debug("Serving {Count} crawl collections from cache (age {Age})", cached.Count, uptime.Elapsed - cachedCollectionsAt);
            return cached;
        }

        using var response = await SendAsync(CollectionsPath, ct);
        if (!response.IsSuccessStatusCode)
        {
            ctxLog.Warn("Failed to read crawl collections: HTTP {Status}", (int)response.StatusCode);
            return [];
        }

        var dtos = await response.Content.ReadFromJsonAsync<List<CrawlCollectionDto>>(ct)
                   ?? [];

        var collections = dtos
            .Where(d => !string.IsNullOrWhiteSpace(d.Id) && !string.IsNullOrWhiteSpace(d.CdxApi))
            .Select(d => new CrawlCollection
            {
                Id = d.Id!,
                Name = d.Name,
                CdxApiUrl = d.CdxApi!,
                Year = ParseYear(d.Id!)
            })
            .OrderByDescending(c => c.Id, StringComparer.Ordinal)
            .ToList();

        cachedCollections = collections;
        cachedCollectionsAt = uptime.Elapsed;

        ctxLog.Info("Read {Count} crawl collections from the index", collections.Count);

        return collections;
    }

    public async Task<CrawlCollection?> GetLatestCollectionAsync(CancellationToken ct) =>
        (await GetCollectionsAsync(ct)).FirstOrDefault();

    /// <exception cref="CrawlIndexUnavailableException">The index never answered - 0 pages would be a lie.</exception>
    public async Task<int> GetPageCountAsync(CrawlIndexQuery query, CancellationToken ct)
    {
        using var response = await SendAsync(BuildUrl(query, page: null, showNumPages: true), ct);

        if (!response.IsSuccessStatusCode)
        {
            // A pattern with zero captures answers 404 - a definitive «nothing here», not a failure.
            ctxLog.Debug(
                "Index reports no pages for {Collection} and '{Pattern}': HTTP {Status}",
                query.Collection.Id, query.UrlPattern, (int)response.StatusCode);

            return 0;
        }

        try
        {
            var pages = await response.Content.ReadFromJsonAsync<CrawlIndexPagesDto>(ct);
            return pages?.Pages ?? 0;
        }
        catch (JsonException)
        {
            // The index answers with plain text when the pattern has no captures at all.
            return 0;
        }
    }

    /// <exception cref="CrawlIndexUnavailableException">The page was never delivered, or the body was cut off.</exception>
    public async IAsyncEnumerable<CrawlIndexRecord> StreamPageAsync(
        CrawlIndexQuery query,
        int page,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var url = BuildUrl(query, page, showNumPages: false);

        using var response = await SendAsync(url, ct);

        if (!response.IsSuccessStatusCode)
        {
            ctxLog.Warn(
                "Index page {Page} of {Collection} is not readable: HTTP {Status}",
                page, query.Collection.Id, (int)response.StatusCode);

            yield break;
        }

        await using var stream = await OpenBodyAsync(response, url, ct);
        using var reader = new StreamReader(stream);

        while (true)
        {
            string? line;

            // A cut-off body is a failed page, not an empty one - the caller must not mark the collection scanned.
            try
            {
                line = await reader.ReadLineAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (CrawlIndexFailure.IsTransient(ex))
            {
                throw new CrawlIndexUnavailableException(url, 1, CrawlIndexFailure.Describe(ex), ex);
            }

            if (line is null)
                break;

            if (line.Length == 0)
                continue;

            var record = TryParseRecord(line);
            if (record is not null)
                yield return record;
        }
    }

    private async Task<Stream> OpenBodyAsync(HttpResponseMessage response, string url, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadAsStreamAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (CrawlIndexFailure.IsTransient(ex))
        {
            throw new CrawlIndexUnavailableException(url, 1, CrawlIndexFailure.Describe(ex), ex);
        }
    }

    private CrawlIndexRecord? TryParseRecord(string line)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<CrawlIndexRecordDto>(line);
            if (string.IsNullOrWhiteSpace(dto?.Url))
                return null;

            return new CrawlIndexRecord
            {
                Url = dto.Url,
            };
        }
        catch (JsonException)
        {
            ctxLog.Debug("Skipped unparsable index line");
            return null;
        }
    }

    private static string BuildUrl(CrawlIndexQuery query, int? page, bool showNumPages)
    {
        var url = $"{query.Collection.CdxApiUrl}?url={Uri.EscapeDataString(CdxPattern(query.UrlPattern))}"
                  + "&output=json&fl=url";

        if (!string.IsNullOrWhiteSpace(query.StatusFilter))
            url += $"&filter={Uri.EscapeDataString($"=status:{query.StatusFilter}")}";

        url += $"&pageSize={query.PageSize}";

        if (showNumPages)
            return url + "&showNumPages=true";

        return page is null ? url : url + $"&page={page}";
    }

    /// <summary>
    /// '*.myworkdayjobs.com/*' asks for a whole domain, and the cdx api reads a leading '*.' as exactly that - but
    /// only when nothing follows the host, so the path part is dropped. Every capture of the domain is then streamed
    /// and the parser is what filters, which it does anyway.
    /// </summary>
    private static string CdxPattern(string pattern)
    {
        if (!pattern.StartsWith("*.", StringComparison.Ordinal))
            return pattern;

        var slash = pattern.IndexOf('/');

        return slash < 0 ? pattern : pattern[..slash];
    }

    /// <summary>
    /// The index front-end throttles hard and answers 503 or drops the connection under load, so retrying is the
    /// normal path. Requests are paced globally and the pace slows down while the index is unhappy.
    /// </summary>
    /// <exception cref="CrawlIndexUnavailableException">Every attempt has failed.</exception>
    private async Task<HttpResponseMessage> SendAsync(string url, CancellationToken ct)
    {
        var opts = options.CurrentValue;
        var attempts = Math.Max(1, opts.IndexRetries + 1);

        await requestGate.WaitAsync(ct);

        try
        {
            var failure = "unknown";
            Exception? lastError = null;

            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                await PaceAsync(opts, ct);

                HttpResponseMessage? response = null;
                TimeSpan? retryHint = null;

                try
                {
                    response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                    lastRequestAt = uptime.Elapsed;

                    if (response.IsSuccessStatusCode || !IsTransient(response.StatusCode))
                    {
                        OnRequestSucceeded(opts);
                        return response;
                    }

                    failure = $"HTTP {(int)response.StatusCode}";
                    retryHint = response.Headers.RetryAfter?.Delta;
                    response.Dispose();
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    ctxLog.Debug("Gracefully finished with cancellation");
                    throw;
                }
                catch (Exception ex) when (CrawlIndexFailure.IsTransient(ex))
                {
                    lastRequestAt = uptime.Elapsed;
                    response?.Dispose();

                    failure = CrawlIndexFailure.Describe(ex);
                    lastError = ex;
                }

                RaiseThrottlePenalty(opts);

                if (attempt == attempts)
                {
                    ctxLog.Warn(
                        lastError,
                        "Crawl index request has failed on the last attempt {Attempt}/{Attempts} ({Failure}): {Url}",
                        attempt, attempts, failure, url);

                    break;
                }

                var delay = NextRetryDelay(opts, attempt, retryHint);

                ctxLog.Warn(
                    lastError,
                    "Crawl index attempt {Attempt}/{Attempts} has failed ({Failure}), retrying in {Delay}. Pacing penalty is {Penalty}: {Url}",
                    attempt, attempts, failure, delay, throttlePenalty, url);

                await Task.Delay(delay, ct);
            }

            throw new CrawlIndexUnavailableException(url, attempts, failure, lastError);
        }
        finally
        {
            requestGate.Release();
        }
    }

    /// <summary>Keeps a minimum gap between requests: the configured one plus whatever throttling has earned us.</summary>
    private async Task PaceAsync(DiscoveryOptions opts, CancellationToken ct)
    {
        var minGap = TimeSpan.FromMilliseconds(Math.Max(0, opts.PauseBetweenRequestsMsec)) + throttlePenalty;
        if (minGap <= TimeSpan.Zero || lastRequestAt == TimeSpan.MinValue)
            return;

        var wait = minGap - (uptime.Elapsed - lastRequestAt);
        if (wait <= TimeSpan.Zero)
            return;

        ctxLog.Debug("Pacing crawl index requests: waiting {Wait} (gap {Gap}, penalty {Penalty})", wait, minGap, throttlePenalty);

        await Task.Delay(wait, ct);
    }

    private TimeSpan NextRetryDelay(DiscoveryOptions opts, int attempt, TimeSpan? retryHint)
    {
        var linear = TimeSpan.FromSeconds(Math.Max(1, opts.IndexRetryDelaySeconds) * attempt);
        var delay = retryHint > linear ? retryHint.Value : linear;
        var max = TimeSpan.FromSeconds(Math.Max(1, opts.MaxIndexRetryDelaySeconds));

        if (retryHint.HasValue)
            ctxLog.Debug("Index has asked to retry after {Hint}", retryHint.Value);

        return delay > max ? max : delay;
    }

    private void RaiseThrottlePenalty(DiscoveryOptions opts)
    {
        successesSincePenalty = 0;

        var step = TimeSpan.FromSeconds(Math.Max(0, opts.ThrottlePenaltyStepSeconds));
        var max = TimeSpan.FromSeconds(Math.Max(0, opts.MaxThrottlePenaltySeconds));
        if (step <= TimeSpan.Zero || throttlePenalty >= max)
            return;

        var raised = throttlePenalty + step;
        throttlePenalty = raised > max ? max : raised;

        ctxLog.Warn("Crawl index looks throttled - pacing penalty raised to {Penalty}", throttlePenalty);
    }

    /// <summary>The request itself is logged by <see cref="LoggingHttpClient"/> - only the pacing state is left here.</summary>
    private void OnRequestSucceeded(DiscoveryOptions opts)
    {
        if (throttlePenalty <= TimeSpan.Zero)
            return;

        var recoverAfter = Math.Max(1, opts.ThrottleRecoveryAfterRequests);
        if (++successesSincePenalty < recoverAfter)
            return;

        successesSincePenalty = 0;

        var step = TimeSpan.FromSeconds(Math.Max(1, opts.ThrottlePenaltyStepSeconds));
        throttlePenalty = throttlePenalty > step ? throttlePenalty - step : TimeSpan.Zero;

        ctxLog.Info(
            "Crawl index has been stable for {Requests} requests - pacing penalty lowered to {Penalty}",
            recoverAfter, throttlePenalty);
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.TooManyRequests
            or HttpStatusCode.RequestTimeout
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    private static int ParseYear(string collectionId)
    {
        var match = YearPattern().Match(collectionId);
        return match.Success
            ? int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture)
            : 0;
    }

    [GeneratedRegex(@"(?<year>(19|20)\d{2})")]
    private static partial Regex YearPattern();
}
