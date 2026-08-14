namespace JobsPulse.Sinks.Telegram.Models;

/// <summary>
/// The geography of a vacancy, coarse enough to group a feed by. The enum order is the display order, and it starts
/// with <see cref="Europe"/> on purpose: that is the region the reader of this bot is looking for, so it leads every
/// list instead of being scrolled to.
/// </summary>
public enum LocationRegion
{
    Europe,
    Remote,
    Cis,
    Americas,
    Asia,
    MiddleEastAndAfrica,
    Oceania,

    /// <summary>Nothing recognizable in the location - shown last, never dropped.</summary>
    Unknown
}
