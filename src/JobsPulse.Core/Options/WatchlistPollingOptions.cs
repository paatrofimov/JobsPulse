using System.ComponentModel.DataAnnotations;

namespace JobsPulse.Core.Options;

public sealed class WatchlistPollingOptions
{
    public const string SectionName = "WatchlistPolling";

    [Range(1, 1440)] public int PollingIntervalMinutes { get; set; } = 10;

    [Range(1, 32)] public int MaxConcurrentEntries { get; set; } = 4;

    [Range(5, 600)] public int SingleEntryProcessTimeoutSeconds { get; set; } = 30;

    // Notifications are not sent
    public bool DryRun { get; set; }
}