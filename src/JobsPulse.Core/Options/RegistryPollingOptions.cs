using System.ComponentModel.DataAnnotations;

namespace JobsPulse.Core.Options;

/// <summary>
/// Background polling of the discovered board registry. Deliberately slower and narrower than the watchlist
/// polling: the watchlist is the priority feed, the registry must not starve it or the discovery crawler.
/// </summary>
public sealed class RegistryPollingOptions
{
    public const string SectionName = "RegistryPolling";

    public bool Enabled { get; set; } = true;

    [Range(1, 1440)] public int CycleIntervalMinutes { get; set; } = 15;

    [Range(1, 1440)] public int StartDelayMinutes { get; set; } = 5;

    /// <summary>How many registry boards one cycle takes. The registry is walked round-robin across cycles.</summary>
    [Range(1, 1000)] public int BoardsPerCycle { get; set; } = 50;

    [Range(1, 8)] public int MaxConcurrentBoards { get; set; } = 2;

    /// <summary>Pause after every board - a soft rate limit shared with the discovery validation traffic.</summary>
    [Range(0, 60000)] public int DelayBetweenBoardsMs { get; set; } = 500;

    [Range(5, 600)] public int SingleEntryProcessTimeoutSeconds { get; set; } = 30;

    /// <summary>Upper bound of the registry slice held in memory per cycle.</summary>
    [Range(1, 100000)] public int MaxRegistryBoards { get; set; } = 20000;

    /// <summary>
    /// Add a registry board to a watchlist when its vacancies pass that watchlist's filter. Only watchlists with a
    /// non-empty filter are ever filled - one matching everything would absorb the whole registry.
    /// </summary>
    public bool AutoAdd { get; set; } = true;

    /// <summary>Safety valve: how many boards one cycle may add. The rest wait for the next cycle.</summary>
    [Range(1, 1000)] public int MaxAutoAddedBoardsPerCycle { get; set; } = 5;

    public bool DryRun { get; set; }
}
