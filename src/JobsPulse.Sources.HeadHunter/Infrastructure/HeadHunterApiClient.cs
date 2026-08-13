using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using JobsPulse.Core.Helpers;
using JobsPulse.Core.Infrastructure;
using JobsPulse.Sources.HeadHunter.Abstractions;
using JobsPulse.Sources.HeadHunter.Models;
using JobsPulse.Sources.HeadHunter.Options;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Sources.HeadHunter.Infrastructure;

/// <summary>
/// Thin client over the public HeadHunter api: one request is one page of employers, one employer, one page of
/// vacancies or one vacancy.
///
/// Unlike the ATS clients this one paces itself. HeadHunter publishes no rate limit for anonymous traffic, so there is
/// no number to stay under - only the api's own reaction to being asked too often. Requests therefore go through one
/// gate that keeps a minimum gap between them, and every throttled or failed answer widens that gap; a run of
/// successful ones narrows it back. Nothing here assumes a particular requests-per-second.
/// </summary>
public sealed class HeadHunterApiClient(
    LoggingHttpClient http,
    IHeadHunterAuthorization authorization,
    IOptionsMonitor<HeadHunterOptions> options,
    ILog log)
{
    public const string HttpClientName = "headhunter";

    /// <summary>Page size ceiling of both searches - the api answers HTTP 400 above it.</summary>
    public const int MaxPageSize = 100;

    private readonly ILog ctxLog = log.ForContext<HeadHunterApiClient>();

    // Every request of the process goes through this gate, so the pace and the penalty are single-threaded.
    private readonly SemaphoreSlim requestGate = new(1, 1);
    private readonly Stopwatch uptime = Stopwatch.StartNew();

    private TimeSpan lastRequestAt = TimeSpan.MinValue;
    private TimeSpan throttlePenalty = TimeSpan.Zero;
    private int successesSincePenalty;

    /// <summary>The employer catalog search - this is what turns a company name into employer ids.</summary>
    public async Task<HeadHunterFetch<EmployerSearchDto>> SearchEmployersAsync(
        string text,
        int page,
        int perPage,
        bool onlyWithVacancies,
        CancellationToken ct)
    {
        var url = new StringBuilder("employers?text=")
            .Append(Uri.EscapeDataString(text))
            .Append("&page=").Append(Math.Max(0, page))
            .Append("&per_page=").Append(Math.Clamp(perPage, 1, MaxPageSize));

        if (onlyWithVacancies)
            url.Append("&only_with_vacancies=true");

        return await GetAsync<EmployerSearchDto>(url.ToString(), ct);
    }

    public async Task<HeadHunterFetch<EmployerDetailDto>> GetEmployerAsync(string employerId, CancellationToken ct) =>
        await GetAsync<EmployerDetailDto>($"employers/{Uri.EscapeDataString(employerId)}", ct);

    /// <summary>
    /// The common vacancy search narrowed to one employer - HeadHunter publishes no per-employer list endpoint, and
    /// `employer_id` on the search is the supported way to ask for a company's board.
    /// </summary>
    public async Task<HeadHunterFetch<VacancySearchDto>> SearchVacanciesAsync(
        HeadHunterVacancyQuery query,
        CancellationToken ct)
    {
        var url = new StringBuilder("vacancies?employer_id=")
            .Append(Uri.EscapeDataString(query.EmployerId))
            .Append("&page=").Append(Math.Max(0, query.Page))
            .Append("&per_page=").Append(Math.Clamp(query.PerPage, 1, MaxPageSize));

        if (!string.IsNullOrWhiteSpace(query.OrderBy))
            url.Append("&order_by=").Append(Uri.EscapeDataString(query.OrderBy));

        if (query.DateTo is { } dateTo)
            url.Append("&date_to=").Append(Uri.EscapeDataString(dateTo.ToUniversalTime().ToString("O")));

        return await GetAsync<VacancySearchDto>(url.ToString(), ct);
    }

    public async Task<HeadHunterFetch<VacancyDetailDto>> GetVacancyAsync(string vacancyId, CancellationToken ct) =>
        await GetAsync<VacancyDetailDto>($"vacancies/{Uri.EscapeDataString(vacancyId)}", ct);

    private async Task<HeadHunterFetch<T>> GetAsync<T>(string relativeUrl, CancellationToken ct) where T : class
    {
        var opts = options.CurrentValue;
        var attempts = Math.Max(1, opts.Retries + 1);
        var token = await authorization.GetAccessTokenAsync(ct);

        await requestGate.WaitAsync(ct);

        try
        {
            var failure = "unknown error";

            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                await PaceAsync(opts, ct);

                HttpResponseMessage? response = null;
                TimeSpan? retryHint = null;

                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl);

                    if (token is not null)
                        request.Headers.Authorization = new("Bearer", token);

                    response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                    lastRequestAt = uptime.Elapsed;

                    if (response.IsSuccessStatusCode)
                    {
                        OnRequestSucceeded(opts);

                        return await ReadAsync<T>(response, relativeUrl, ct);
                    }

                    if (!IsTransient(response.StatusCode))
                    {
                        // A definitive answer is not a reason to slow down - only a throttled one is.
                        OnRequestSucceeded(opts);

                        return await DescribeAsync<T>(response, relativeUrl, ct);
                    }

                    failure = $"HTTP {(int)response.StatusCode}";
                    retryHint = response.Headers.RetryAfter?.Delta;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
                {
                    lastRequestAt = uptime.Elapsed;
                    failure = $"{ex.GetType().Name}: {ex.Message}";
                }
                finally
                {
                    response?.Dispose();
                }

                RaiseThrottlePenalty(opts);

                if (attempt == attempts)
                    break;

                var delay = NextRetryDelay(opts, attempt, retryHint);

                ctxLog.Warn(
                    "HeadHunter attempt {Attempt}/{Attempts} has failed ({Failure}), retrying in {Delay}. Pacing penalty is {Penalty}: {Url}",
                    attempt, attempts, failure, delay, throttlePenalty, relativeUrl);

                await Task.Delay(delay, ct);
            }

            return HeadHunterFetch<T>.Failure(failure);
        }
        finally
        {
            requestGate.Release();
        }
    }

    private static async Task<HeadHunterFetch<T>> ReadAsync<T>(
        HttpResponseMessage response,
        string relativeUrl,
        CancellationToken ct) where T : class
    {
        try
        {
            var payload = await response.Content.ReadFromJsonAsync<T>(JsonSerializerOptionsFactory.Instance, ct);

            return payload is null
                ? HeadHunterFetch<T>.Failure($"empty response: {relativeUrl}")
                : HeadHunterFetch<T>.Ok(payload);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or IOException)
        {
            // A body that no longer deserializes is a contract error, deliberately not a missing employer.
            return HeadHunterFetch<T>.Failure($"contract error: {ex.Message}");
        }
    }

    /// <summary>
    /// Turns a definitive non-success answer into a fetch. The api spells its refusals out in the body, which matters
    /// twice: the vacancy search answers HTTP 400 `bad_argument` for an employer id it does not know instead of a 404,
    /// and a 403 is either an anti-bot decision or an endpoint that stopped being public - a token question, not a
    /// board that disappeared.
    /// </summary>
    private async Task<HeadHunterFetch<T>> DescribeAsync<T>(
        HttpResponseMessage response,
        string relativeUrl,
        CancellationToken ct) where T : class
    {
        var status = (int)response.StatusCode;
        var error = await ReadErrorAsync(response, ct);
        var described = error is null ? $"HTTP {status}" : $"HTTP {status} ({error.Describe()})";

        if (response.StatusCode == HttpStatusCode.NotFound)
            return HeadHunterFetch<T>.Missing();

        if (response.StatusCode == HttpStatusCode.BadRequest && error?.NamesUnknownEmployer() == true)
        {
            ctxLog.Debug("HeadHunter does not know the employer asked for: {Url}", relativeUrl);

            return HeadHunterFetch<T>.Missing();
        }

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            ctxLog.Warn(
                "HeadHunter has refused the request ({Error}). The public endpoints need no token; if this persists, "
                + "an application token in 'Sources:HeadHunter:AccessToken' is what the api is asking for: {Url}",
                described, relativeUrl);

            return HeadHunterFetch<T>.Refused(described);
        }

        return HeadHunterFetch<T>.Failure(described);
    }

    private static async Task<HeadHunterErrorDto?> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<HeadHunterErrorDto>(JsonSerializerOptionsFactory.Instance, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or IOException)
        {
            return null;
        }
    }

    /// <summary>Keeps a minimum gap between requests: the configured one plus whatever throttling has earned us.</summary>
    private async Task PaceAsync(HeadHunterOptions opts, CancellationToken ct)
    {
        var minGap = TimeSpan.FromMilliseconds(Math.Max(0, opts.PauseBetweenRequestsMsec)) + throttlePenalty;
        if (minGap <= TimeSpan.Zero || lastRequestAt == TimeSpan.MinValue)
            return;

        var wait = minGap - (uptime.Elapsed - lastRequestAt);
        if (wait <= TimeSpan.Zero)
            return;

        await Task.Delay(wait, ct);
    }

    private TimeSpan NextRetryDelay(HeadHunterOptions opts, int attempt, TimeSpan? retryHint)
    {
        var linear = TimeSpan.FromSeconds(Math.Max(1, opts.RetryDelaySeconds) * attempt);
        var delay = retryHint > linear ? retryHint.Value : linear;
        var max = TimeSpan.FromSeconds(Math.Max(1, opts.MaxRetryDelaySeconds));

        return delay > max ? max : delay;
    }

    private void RaiseThrottlePenalty(HeadHunterOptions opts)
    {
        successesSincePenalty = 0;

        var step = TimeSpan.FromSeconds(Math.Max(0, opts.ThrottlePenaltyStepSeconds));
        var max = TimeSpan.FromSeconds(Math.Max(0, opts.MaxThrottlePenaltySeconds));
        if (step <= TimeSpan.Zero || throttlePenalty >= max)
            return;

        var raised = throttlePenalty + step;
        throttlePenalty = raised > max ? max : raised;

        ctxLog.Warn("HeadHunter looks throttled - pacing penalty raised to {Penalty}", throttlePenalty);
    }

    /// <summary>The request itself is logged by <see cref="LoggingHttpClient"/> - only the pacing state is left here.</summary>
    private void OnRequestSucceeded(HeadHunterOptions opts)
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
            "HeadHunter has been stable for {Requests} requests - pacing penalty lowered to {Penalty}",
            recoverAfter, throttlePenalty);
    }

    /// <summary>
    /// 403 is deliberately not here: it is the api's verdict on the caller, and every retry of it is one more request
    /// against whatever limit produced it.
    /// </summary>
    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.TooManyRequests
            or HttpStatusCode.RequestTimeout
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;
}
