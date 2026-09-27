namespace JobsPulse.Discovery.Models;

/// <summary>
/// The crawl index has been throttling us so hard that the pacing penalty reached
/// <c>Discovery:GiveUpAtThrottlePenaltySeconds</c>. Deliberately not transient: carrying on means minutes of waiting per
/// request, so the http pass stops for this run and leaves the rest pending.
/// </summary>
public sealed class CrawlIndexThrottledException(TimeSpan penalty)
    : Exception($"Crawl index is throttled - the pacing penalty has reached {penalty}")
{
    public TimeSpan Penalty { get; } = penalty;
}
