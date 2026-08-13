namespace JobsPulse.Sources.SuccessFactors.Models;

/// <summary>
/// The career site feed: one rss document holding the whole board. The channel carries the two things worth keeping
/// besides the items - the site's own name, which is what the company is called on its career site, and the locale it
/// publishes in. The feed is single-locale, so an item can never appear in it twice.
/// </summary>
public sealed record JobFeedDto
{
    public string? Title { get; init; }

    /// <summary>Locale of the site as it reports it - 'en_US', 'de_DE'.</summary>
    public string? Language { get; init; }

    public IReadOnlyList<JobFeedItemDto> Items { get; init; } = [];
}
