namespace JobsPulse.Sources.HeadHunter.Infrastructure;

/// <summary>
/// The user agent is part of the HeadHunter contract, and the api does not merely dislike a missing one: it keeps a
/// blacklist, and everything on it is answered HTTP 400 `bad_user_agent:blacklisted` whatever else was right about the
/// request. What lands on that blacklist is the placeholder contact of the documentation's own examples - the agent that
/// every project copies - so a configured agent is checked here rather than trusted.
/// </summary>
public static class HeadHunterUserAgent
{
    public const string Default = "JobsPulse/1.0 (+https://github.com/patrofimov/JobsPulse)";

    // Not the api's list - it is not published. These are the placeholder contacts of its examples, which is what a
    // copied agent carries and the only part of the blacklist that can be recognized without asking.
    private static readonly string[] PlaceholderMarkers =
    [
        "example.com",
        "example.org",
        "example.net",
        "yourcompany",
        "your-company",
        "your-app",
        "user@mail",
        "mail@mail"
    ];

    /// <summary>
    /// The agent to send: whatever was configured, unless it is empty or carries a placeholder contact - in which case
    /// it would be refused, and <see cref="Default"/> is sent instead.
    /// </summary>
    public static string Resolve(string? configured) =>
        IsAcceptable(configured) ? configured!.Trim() : Default;

    /// <summary>Whether the agent stands a chance at all - see <see cref="Resolve"/>.</summary>
    public static bool IsAcceptable(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
            return false;

        return !PlaceholderMarkers.Any(marker => configured.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }
}
