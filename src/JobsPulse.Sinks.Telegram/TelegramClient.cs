using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace JobsPulse.Sinks.Telegram;

/// <summary>
/// Минимальный клиент Bot API: sendMessage + getUpdates (long polling).
/// Намеренно без библиотеки — на этапе каркаса это одна зависимость меньше;
/// при усложнении бота заменяется на Telegram.Bot без изменения вызывающего кода.
/// </summary>
public sealed class TelegramClient(HttpClient http, IOptions<TelegramOptions> options)
{
    public const string HttpClientName = "telegram";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private string Base => $"/bot{options.Value.BotToken}";

    public async Task<TelegramResult> SendMessageAsync(
        string chatId, string html, bool silent, CancellationToken ct)
    {
        var payload = new
        {
            chat_id = chatId,
            text = html,
            parse_mode = "HTML",
            disable_notification = silent,
            link_preview_options = new { is_disabled = true }
        };

        try
        {
            using var response = await http.PostAsJsonAsync($"{Base}/sendMessage", payload, Json, ct);
            var body = await response.Content.ReadFromJsonAsync<ApiResponse<object>>(Json, ct);

            if (response.IsSuccessStatusCode && body is { Ok: true })
                return TelegramResult.Ok;

            // 429: Telegram сам говорит, сколько ждать. Уважаем.
            var retryAfter = body?.Parameters?.RetryAfter is { } seconds
                ? TimeSpan.FromSeconds(seconds)
                : (TimeSpan?)null;

            return TelegramResult.Fail(body?.Description ?? $"HTTP {(int)response.StatusCode}", retryAfter);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return TelegramResult.Fail(ex.Message);
        }
    }

    public async Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(
        long offset, int timeoutSeconds, CancellationToken ct)
    {
        var url = $"{Base}/getUpdates?offset={offset}&timeout={timeoutSeconds}&allowed_updates=[\"message\"]";

        using var response = await http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode) return [];

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<List<TelegramUpdate>>>(Json, ct);
        return body is { Ok: true, Result: not null } ? body.Result : [];
    }

    private sealed class ApiResponse<T>
    {
        [JsonPropertyName("ok")] public bool Ok { get; set; }
        [JsonPropertyName("result")] public T? Result { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("parameters")] public ResponseParameters? Parameters { get; set; }
    }

    private sealed class ResponseParameters
    {
        [JsonPropertyName("retry_after")] public int? RetryAfter { get; set; }
    }
}

public sealed class TelegramUpdate
{
    [JsonPropertyName("update_id")] public long UpdateId { get; set; }
    [JsonPropertyName("message")] public TelegramMessage? Message { get; set; }
}

public sealed class TelegramMessage
{
    [JsonPropertyName("text")] public string? Text { get; set; }
    [JsonPropertyName("chat")] public TelegramChat? Chat { get; set; }
}

public sealed class TelegramChat
{
    [JsonPropertyName("id")] public long Id { get; set; }
}

public readonly record struct TelegramResult(bool Success, string? Error, TimeSpan? RetryAfter)
{
    public static readonly TelegramResult Ok = new(true, null, null);

    public static TelegramResult Fail(string error, TimeSpan? retryAfter = null) => new(false, error, retryAfter);
}
