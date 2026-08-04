using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JobsPulse.Sources.Greenhouse.Dto;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobsPulse.Sources.Greenhouse;

/// <summary>
/// Тонкая обёртка над Job Board API. Знает только про HTTP и DTO.
///
/// Ограничения самого API, отражённые здесь:
///  • нет серверной фильтрации — борд всегда приходит целиком;
///  • нет пагинации у /jobs;
///  • официального rate limit нет, но он наверняка есть — темп ограничивается снаружи, в оркестраторе.
/// </summary>
public sealed class GreenhouseBoardClient(
    HttpClient http,
    IOptions<GreenhouseOptions> options,
    ILogger<GreenhouseBoardClient> log)
{
    public const string HttpClientName = "greenhouse";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly GreenhouseOptions _options = options.Value;

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
                log.LogWarning("Greenhouse ответил 429 на {Url}, просит подождать {Delay}", relativeUrl, retryAfter);
                return BoardFetch<T>.Failure($"rate limited, retry after {retryAfter.TotalSeconds:F0}s");
            }

            if (!response.IsSuccessStatusCode)
                return BoardFetch<T>.Failure($"HTTP {(int)response.StatusCode}");

            var payload = await response.Content.ReadFromJsonAsync<T>(Json, ct);
            return payload is null
                ? BoardFetch<T>.Failure("пустой ответ")
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

/// <summary>
/// Результат запроса с явным различением «нет борда» и «не смогли получить».
/// Смешивать их нельзя: первое — повод отключить запись, второе — повод повторить.
/// </summary>
public readonly record struct BoardFetch<T>(T? Value, bool NotFound, string? Error) where T : class
{
    public bool Success => Value is not null;

    public static BoardFetch<T> Ok(T value) => new(value, false, null);
    public static BoardFetch<T> Missing() => new(null, true, "борд не найден");
    public static BoardFetch<T> Failure(string error) => new(null, false, error);
}
