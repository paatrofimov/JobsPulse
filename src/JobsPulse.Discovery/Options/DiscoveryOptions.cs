namespace JobsPulse.Discovery.Options;

public sealed class DiscoveryOptions
{
    public const string SectionName = "Discovery";

    public bool Enabled { get; set; } = true;

    public string IndexBaseUrl { get; set; } = "https://index.commoncrawl.org/";

    /// <summary>How many years of crawl indexes the bootstrap union covers.</summary>
    public int BootstrapYears { get; set; } = 1;

    /// <summary>Delay before the first run, so the polling pipeline starts first.</summary>
    public int StartDelayMinutes { get; set; } = 2;

    public int RunIntervalHours { get; set; } = 24;

    /// <summary>0 - no cap. A single collection can hold hundreds of index pages.</summary>
    public int MaxPagesPerCollection { get; set; }

    public int PageSize { get; set; } = 5;

    /// <summary>Safety valve - a run stops collecting tokens after this many unknown ones.</summary>
    public int MaxNewTokensPerRun { get; set; } = 5000;

    public int ValidationConcurrency { get; set; } = 4;

    public int UpsertBatchSize { get; set; } = 200;

    public int IndexRetries { get; set; } = 3;

    public int IndexRetryDelaySeconds { get; set; } = 10;
}
