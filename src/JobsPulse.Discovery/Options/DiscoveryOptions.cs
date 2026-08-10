namespace JobsPulse.Discovery.Options;

public sealed class DiscoveryOptions
{
    public const string SectionName = "Discovery";

    public bool Enabled { get; set; } = false;

    public string IndexBaseUrl { get; set; } = "https://index.commoncrawl.org/";

    /// <summary>How many years of crawl indexes the bootstrap union covers.</summary>
    public int BootstrapYears { get; set; } = 1;

    /// <summary>Delay before the first run, so the polling pipeline starts first.</summary>
    public int StartDelayMinutes { get; set; } = 5;

    public int RunIntervalHours { get; set; } = 24;

    public long? PauseBetweenPagesMsec { get; set; } = 1000;

    /// <summary>Pause after a collection is finished or abandoned, on top of the per-request pacing.</summary>
    public long PauseBetweenCollectionsMsec { get; set; } = 2000;

    /// <summary>Minimum gap between any two crawl index requests. The index throttles hard, so it is never 0.</summary>
    public long PauseBetweenRequestsMsec { get; set; } = 1000;

    /// <summary>0 - no cap. A single collection can hold hundreds of index pages.</summary>
    public int MaxPagesPerCollection { get; set; }

    public int PageSize { get; set; } = 5;

    /// <summary>Safety valve - a run stops collecting tokens after this many unknown ones.</summary>
    public int MaxNewTokensPerRun { get; set; } = 5000;

    public int ValidationConcurrency { get; set; } = 4;

    public int UpsertBatchSize { get; set; } = 200;

    public int IndexRetries { get; set; } = 3;

    public int IndexRetryDelaySeconds { get; set; } = 60;

    /// <summary>Upper bound for one retry delay - the linear/hinted backoff never grows past it.</summary>
    public int MaxIndexRetryDelaySeconds { get; set; } = 6000;

    /// <summary>How much the pacing penalty grows after every throttled or failed request.</summary>
    public int ThrottlePenaltyStepSeconds { get; set; } = 60;

    public int MaxThrottlePenaltySeconds { get; set; } = 1200;

    /// <summary>How many requests in a row must succeed before the pacing penalty is relaxed by one step.</summary>
    public int ThrottleRecoveryAfterRequests { get; set; } = 10;

    /// <summary>Failed pages tolerated inside one collection before it is abandoned and left pending.</summary>
    public int MaxPageFailuresPerCollection { get; set; } = 3;

    /// <summary>Collections failing in a row before the source is given up on - the index is clearly down.</summary>
    public int MaxConsecutiveCollectionFailures { get; set; } = 5;

    /// <summary>`collinfo.json` changes a few times a year - re-reading it on every run is wasted traffic.</summary>
    public int CollectionsCacheMinutes { get; set; } = 60;
}
