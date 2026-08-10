namespace JobsPulse.Sources.Lever.Models;

/// <summary>
/// A Lever instance. Lever runs the same API on a global and an EU deployment, and a company site exists on exactly
/// one of them - same paths, same payloads, different host, which is why one client serves both.
/// </summary>
public sealed record LeverRegion
{
    public required string Id { get; init; }

    /// <summary>Base url of the postings API, ending with a slash.</summary>
    public required string PostingsApiUrl { get; init; }

    /// <summary>Host of the public job board of this instance.</summary>
    public required string JobsHost { get; init; }

    public static readonly LeverRegion Global = new()
    {
        Id = "global",
        PostingsApiUrl = "https://api.lever.co/v0/postings/",
        JobsHost = "jobs.lever.co"
    };

    public static readonly LeverRegion Eu = new()
    {
        Id = "eu",
        PostingsApiUrl = "https://api.eu.lever.co/v0/postings/",
        JobsHost = "jobs.eu.lever.co"
    };

    public static readonly IReadOnlyList<LeverRegion> All = [Global, Eu];

    public static LeverRegion? Find(string id) =>
        All.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));

    public string BoardUrl(string site) => $"https://{JobsHost}/{site}";

    public string PostingUrl(string site, string postId) => $"https://{JobsHost}/{site}/{postId}";
}
