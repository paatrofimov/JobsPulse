using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JobsPulse.Sources.Greenhouse.Models;
using Microsoft.Extensions.Logging;

namespace JobsPulse.Sources.Greenhouse.Infrastructure;

public sealed class GreenhouseBoardClient(
    HttpClient http,
    ILogger<GreenhouseBoardClient> log)
{
    public const string HttpClientName = "greenhouse";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<BoardFetch<JobListResponse>> GetJobsAsync(
        string boardToken, bool includeContent, CancellationToken ct)
    {
        var url = $"{Uri.EscapeDataString(boardToken)}/jobs" + (includeContent ? "?content=true" : string.Empty);
        return await GetAsync<JobListResponse>(url, ct);
    }

    public async Task<BoardFetch<BoardDto>> GetBoardAsync(string boardToken, CancellationToken ct) =>
        await GetAsync<BoardDto>(Uri.EscapeDataString(boardToken), ct);

    private async Task<BoardFetch<T>> GetAsync<T>(string relativeUrl, CancellationToken ct) where T : class
    {
        try
        {
            using var response = await http.GetAsync(relativeUrl, HttpCompletionOption.ResponseHeadersRead, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return BoardFetch<T>.Missing();

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(30);
                log.LogWarning("Greenhouse is throttling 429: {Url}, asked to wait for: {Delay}", relativeUrl, retryAfter);
                return BoardFetch<T>.Failure($"rate limited, retry after {retryAfter.TotalSeconds:F0}s");
            }

            if (!response.IsSuccessStatusCode)
                return BoardFetch<T>.Failure($"HTTP {(int)response.StatusCode}");

            var payload = await response.Content.ReadFromJsonAsync<T>(Json, ct);
            return payload is null
                ? BoardFetch<T>.Failure("empty response")
                : BoardFetch<T>.Ok(payload);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return BoardFetch<T>.Failure(ex.Message);
        }
    }
}