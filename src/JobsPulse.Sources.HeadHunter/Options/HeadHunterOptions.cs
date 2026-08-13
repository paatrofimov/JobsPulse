using JobsPulse.Sources.HeadHunter.Infrastructure;

namespace JobsPulse.Sources.HeadHunter.Options;

public sealed class HeadHunterOptions
{
    public const string SectionName = "Sources:HeadHunter";

    public string BaseUrl { get; set; } = "https://api.hh.ru/";

    /// <summary>
    /// HeadHunter rejects a request whose user agent it does not like with HTTP 400 `bad_user_agent`, so this is not
    /// cosmetic: it has to name the application and a way to reach whoever runs it. The blacklist covers the placeholder
    /// contacts of the api's own examples (`example.com` and friends), so a copied sample agent is refused - see
    /// <see cref="HeadHunterUserAgent"/>, which is what a placeholder is replaced by.
    /// </summary>
    public string UserAgent { get; set; } = HeadHunterUserAgent.Default;

    /// <summary>
    /// Bearer token of a registered HeadHunter application. Since April 2026 the search endpoints answer HTTP 403
    /// `forbidden` to anonymous callers - only the dictionaries stayed public - so a working installation needs one;
    /// `IHeadHunterAuthorization` is the seam it is asked for.
    /// </summary>
    public string? AccessToken { get; set; }

    /// <summary>Full descriptions live on the per-vacancy endpoint, so they cost one request each.</summary>
    public bool IncludeContentOnPoll { get; set; }

    /// <summary>Page size of the vacancy search (`per_page`). HeadHunter caps it at 100.</summary>
    public int PageSize { get; set; } = 100;

    /// <summary>Safety cap on pagination - an employer bigger than this is an incomplete traversal.</summary>
    public int MaxPages { get; set; } = 40;

    /// <summary>
    /// How deep `page` * `per_page` may go before the search refuses the request. Not a safety cap but the api's own
    /// ceiling, and the reason paging continues in publication-date windows instead of asking for page 21.
    /// </summary>
    public int MaxPagedItems { get; set; } = 2000;

    /// <summary>
    /// How many date windows one traversal may spend. Every window re-reads the boundary page, so an employer needing
    /// more than a handful of them is better reported as incomplete than paged forever.
    /// </summary>
    public int MaxDateWindows { get; set; } = 20;

    /// <summary>Upper bound on description requests per traversal - the rest is mapped from the search snippet.</summary>
    public int MaxDescriptionRequests { get; set; } = 100;

    /// <summary>How the vacancy search is sorted. Date windowing needs a publication-time order to walk backwards.</summary>
    public string OrderBy { get; set; } = "publication_time";

    /// <summary>Employers per page of the employer search - one page is read, so this is the whole candidate set.</summary>
    public int EmployerSearchPageSize { get; set; } = 20;

    /// <summary>How many ranked employers a name lookup may offer. The bot shows them as a choice.</summary>
    public int MaxEmployerCandidates { get; set; } = 5;

    /// <summary>
    /// Score below which an employer is not a plausible answer at all. The search is fuzzy on purpose - it is what
    /// matches 'Yandex' to 'Яндекс' - so the tail of its results has nothing to do with the company asked for.
    /// </summary>
    public int MinMatchScore { get; set; } = 45;

    /// <summary>
    /// How far ahead of the runner-up the best match has to be to be answered on its own. Below the gap the result is
    /// ambiguous and every candidate is offered instead of one of them being guessed.
    /// </summary>
    public int DecisiveScoreGap { get; set; } = 20;

    /// <summary>
    /// Whether the employer search asks for employers that have open vacancies. An employer with none is a catalog
    /// page rather than a board, and cannot be told from a wrong match by anything we could poll.
    /// </summary>
    public bool OnlyEmployersWithVacancies { get; set; } = true;

    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Minimum gap between two requests. HeadHunter documents no public rate limit, so this is a starting pace rather
    /// than a known ceiling - the adaptive penalty below is what actually keeps the client inside the real limit.
    /// </summary>
    public int PauseBetweenRequestsMsec { get; set; } = 250;

    public int Retries { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 5;
    public int MaxRetryDelaySeconds { get; set; } = 60;

    /// <summary>How much the pace slows down after a throttled or failed request.</summary>
    public int ThrottlePenaltyStepSeconds { get; set; } = 2;

    public int MaxThrottlePenaltySeconds { get; set; } = 30;

    /// <summary>Successful requests in a row after which one penalty step is given back.</summary>
    public int ThrottleRecoveryAfterRequests { get; set; } = 20;
}
