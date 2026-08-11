using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using JobsPulse.Core.Helpers;
using JobsPulse.Core.Infrastructure;
using JobsPulse.Sources.Workday.Models;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Sources.Workday.Infrastructure;

/// <summary>
/// Thin client over the backend of the public Workday careers site (`/wday/cxs/{tenant}/{site}`). This is what the
/// job board frontend itself calls - no credentials, no enterprise api. The list is a POST with a paging body and
/// carries no description; the description and the only real date live on the per-vacancy endpoint.
/// </summary>
public sealed class WorkdayCxsClient(
    LoggingHttpClient http,
    ILog log)
{
    /// <summary>The list endpoint rejects anything above this with HTTP 400.</summary>
    public const int MaxPageSize = 20;

    private readonly ILog ctxLog = log.ForContext<WorkdayCxsClient>();

    public async Task<WorkdayFetch<JobsPageDto>> GetJobsAsync(
        WorkdayBoardConfig config,
        int offset,
        int limit,
        CancellationToken ct)
    {
        var url = $"{config.CxsBaseUrl}/jobs";

        var body = new
        {
            appliedFacets = new { },
            limit = Math.Clamp(limit, 1, MaxPageSize),
            offset = Math.Max(0, offset),
            searchText = string.Empty
        };

        using var content = new StringContent(
            JsonSerializer.Serialize(body, JsonSerializerOptionsFactory.Instance),
            Encoding.UTF8,
            "application/json");

        return await SendAsync<JobsPageDto>(() => http.PostAsync(url, content, ct), url, ct);
    }

    /// <summary><paramref name="externalPath"/> is taken from a posting and already starts with '/job/'.</summary>
    public async Task<WorkdayFetch<JobDetailDto>> GetJobAsync(
        WorkdayBoardConfig config,
        string externalPath,
        CancellationToken ct)
    {
        var url = config.CxsJobUrl(externalPath);

        return await SendAsync<JobDetailDto>(
            () => http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct), url, ct);
    }

    private async Task<WorkdayFetch<T>> SendAsync<T>(
        Func<Task<HttpResponseMessage>> send,
        string url,
        CancellationToken ct) where T : class
    {
        try
        {
            using var response = await send();

            // 404 - the tenant is fine but the site is not there. 422 - Workday does not know the tenant at all.
            // Both are honest 'this board does not exist' answers; nothing else is.
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity)
                return WorkdayFetch<T>.Missing();

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(30);
                ctxLog.Warn("Workday is throttling 429: {Url}, asked to wait for: {Delay}", url, retryAfter);

                return WorkdayFetch<T>.Failure($"rate limited, retry after {retryAfter.TotalSeconds:F0}s");
            }

            if (!response.IsSuccessStatusCode)
                return WorkdayFetch<T>.Failure($"HTTP {(int)response.StatusCode}");

            var payload = await response.Content.ReadFromJsonAsync<T>(JsonSerializerOptionsFactory.Instance, ct);

            return payload is null
                ? WorkdayFetch<T>.Failure("empty response")
                : WorkdayFetch<T>.Ok(payload);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (JsonException ex)
        {
            // A contract change must surface as an error, never as a board that stopped existing.
            ctxLog.Warn(ex, "Workday answered {Url} with a body that no longer deserializes", url);

            return WorkdayFetch<T>.Failure($"contract error: {ex.Message}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return WorkdayFetch<T>.Failure(ex.Message);
        }
    }
}
