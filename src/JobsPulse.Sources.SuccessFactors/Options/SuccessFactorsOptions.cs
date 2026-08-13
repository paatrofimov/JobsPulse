namespace JobsPulse.Sources.SuccessFactors.Options;

public sealed class SuccessFactorsOptions
{
    public const string SectionName = "Sources:SuccessFactors";

    /// <summary>
    /// Descriptions arrive with the list whether they were asked for or not, so unlike the other sources this costs
    /// no extra request - only the memory of keeping them.
    /// </summary>
    public bool IncludeContentOnPoll { get; set; }

    /// <summary>
    /// File name the whole-board feed is requested under. Any path the career site does not recognize as one of its
    /// own pages is routed to the feed servlet, so this is a name we pick rather than a route the site publishes -
    /// and it has to stay a name no site uses for something else, which is why '/sitemap.xml' is not it.
    /// </summary>
    public string FeedPath { get; set; } = "jobfeed.xml";

    /// <summary>
    /// Byte budget for one feed. The feed always embeds full descriptions and supports no way to ask it not to, so a
    /// large board is genuinely tens of megabytes. A board over the budget is not a failure - it falls back to
    /// <see cref="EnableHtmlFallback"/> - but it is never committed from a truncated document.
    /// </summary>
    public long MaxFeedBytes { get; set; } = 48L * 1024 * 1024;

    /// <summary>
    /// Whether a board whose feed is unavailable or does not fit the budget is served by paging the site's html job
    /// list instead. Complete, but the tiles it reads are configurable per customer, so the fields are best-effort.
    /// </summary>
    public bool EnableHtmlFallback { get; set; } = true;

    /// <summary>Tiles per html request. Fixed by the endpoint - it pages in steps of 25 whatever is asked.</summary>
    public int HtmlPageSize { get; set; } = 25;

    /// <summary>
    /// Safety cap on html pagination. A page holds 25 vacancies, so the default covers a 10000-vacancy board; a
    /// bigger one is reported as an incomplete traversal and its state is left untouched.
    /// </summary>
    public int MaxPages { get; set; } = 400;

    public int RequestTimeoutSeconds { get; set; } = 120;
}
