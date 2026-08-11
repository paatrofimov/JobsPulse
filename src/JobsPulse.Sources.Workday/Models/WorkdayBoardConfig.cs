using System.Text.Json;
using System.Text.Json.Serialization;
using JobsPulse.Core.Helpers;

namespace JobsPulse.Sources.Workday.Models;

/// <summary>
/// The address of a Workday careers site. Unlike every other supported ATS a single slug is not enough: the host
/// carries the cluster, the tenant is a separate identifier that the host does not always contain, and the site is
/// one of possibly several boards of that tenant. Stored as the board configuration json.
/// </summary>
public sealed record WorkdayBoardConfig
{
    public required string Host { get; init; }

    public required string Tenant { get; init; }

    public required string Site { get; init; }

    public WorkdayHostKind Kind { get; init; } = WorkdayHostKind.MyWorkdayJobs;

    /// <summary>The board identity inside the source - derived, never parsed by the client code.</summary>
    [JsonIgnore] public string BoardId => $"{Host}/{Tenant}/{Site}";

    /// <summary>Public careers site of the board, never the CXS backend.</summary>
    [JsonIgnore]
    public string BoardUrl => Kind == WorkdayHostKind.MyWorkdaySite
        ? $"https://{Host}/recruiting/{Tenant}/{Site}"
        : $"https://{Host}/{Site}";

    /// <summary>Backend of the public job board: https://{host}/wday/cxs/{tenant}/{site}.</summary>
    [JsonIgnore] public string CxsBaseUrl => $"https://{Host}/wday/cxs/{Tenant}/{Site}";

    /// <summary><paramref name="externalPath"/> comes from the api and already starts with '/job/'.</summary>
    public string JobUrl(string externalPath) => $"{BoardUrl}{externalPath}";

    public string CxsJobUrl(string externalPath) => $"{CxsBaseUrl}{externalPath}";

    public string ToJson() => JsonSerializer.Serialize(this, JsonSerializerOptionsFactory.Instance);

    /// <summary>Returns null on anything that is not a readable configuration, including a null input.</summary>
    public static WorkdayBoardConfig? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            var config = JsonSerializer.Deserialize<WorkdayBoardConfig>(json, JsonSerializerOptionsFactory.Instance);

            return config is null || !config.IsComplete()
                ? null
                : config;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The fallback for a board that has no stored configuration - a row written before the column existed, or one
    /// added by hand as '/board_add {watchlist} workday {host}/{tenant}/{site}'.
    /// </summary>
    public static WorkdayBoardConfig? FromBoardId(string? boardId)
    {
        if (string.IsNullOrWhiteSpace(boardId))
            return null;

        var parts = boardId.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 3)
            return null;

        var config = new WorkdayBoardConfig
        {
            Host = parts[0].ToLowerInvariant(),
            Tenant = parts[1],
            Site = parts[2],
            Kind = parts[0].Contains("myworkdaysite.com", StringComparison.OrdinalIgnoreCase)
                ? WorkdayHostKind.MyWorkdaySite
                : WorkdayHostKind.MyWorkdayJobs
        };

        return config.IsComplete() ? config : null;
    }

    private bool IsComplete() =>
        !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(Tenant) && !string.IsNullOrWhiteSpace(Site);
}
