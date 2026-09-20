namespace JobsPulse.Sinks.Telegram.Models;

/// <summary>
/// How hot a company is, in four bands. Enum order is the display order - the hottest band leads, because the whole
/// grouping exists to answer «where is something actually happening».
/// </summary>
public enum ActivityRank
{
    Blazing,
    Hot,
    Warm,
    Still
}
