using System.Text.Json.Serialization;

namespace JobsPulse.Sources.HeadHunter.Models;

/// <summary>
/// The body of a refusal: `{ "description": "...", "errors": [ { "type": "bad_argument", "value": "employer_id" } ] }`.
/// It is read rather than logged as text because the status code alone does not say what happened - see
/// `HeadHunterApiClient.DescribeAsync`.
/// </summary>
public sealed class HeadHunterErrorDto
{
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("errors")] public List<HeadHunterErrorItemDto>? Errors { get; set; }

    public string Describe()
    {
        var reasons = (Errors ?? [])
            .Select(e => string.Join(':', new[] { e.Type, e.Value }.Where(p => !string.IsNullOrWhiteSpace(p))))
            .Where(r => r.Length > 0)
            .ToList();

        var described = string.Join(", ", reasons);

        if (!string.IsNullOrWhiteSpace(Description))
            return described.Length == 0 ? Description!.Trim() : $"{Description!.Trim()}: {described}";

        return described.Length == 0 ? "no reason given" : described;
    }

    /// <summary>
    /// Whether the refusal is «there is no such employer». The vacancy search validates `employer_id` as an argument,
    /// so an employer that never existed - or one that was deleted - is HTTP 400 and not a 404.
    /// </summary>
    public bool NamesUnknownEmployer() =>
        (Errors ?? []).Any(e =>
            string.Equals(e.Value, "employer_id", StringComparison.OrdinalIgnoreCase)
            && e.Type is "bad_argument" or "not_found");
}

public sealed class HeadHunterErrorItemDto
{
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("value")] public string? Value { get; set; }
}
