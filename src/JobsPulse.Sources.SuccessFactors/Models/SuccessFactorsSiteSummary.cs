namespace JobsPulse.Sources.SuccessFactors.Models;

/// <summary>
/// What the seo sitemap of a career site says about the board without the board being downloaded: how many vacancies
/// there are and where each of them lives. That is all a probe needs, and it is roughly forty times cheaper than the
/// feed - a four thousand vacancy board is under a megabyte here against tens of megabytes there, because the feed
/// embeds every description and offers no way to ask it not to.
/// </summary>
public sealed record SuccessFactorsSiteSummary
{
    public required int JobCount { get; init; }

    /// <summary>Public job urls, in sitemap order. Empty when the sitemap turned out to be the feed.</summary>
    public IReadOnlyList<string> JobUrls { get; init; } = [];

    /// <summary>
    /// The site's own name. Only the feed carries one, so this is null whenever the sitemap was the cheap url list.
    /// </summary>
    public string? Title { get; init; }
}
