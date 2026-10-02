namespace JobsPulse.Sinks.Telegram.Models;

/// <param name="Total">Boards in the set.</param>
/// <param name="Fresh">Boards polled at or after <paramref name="FreshSince"/>.</param>
/// <param name="Never">Boards without a single poll stamp.</param>
/// <param name="Oldest">The oldest poll stamp in the set; null when nothing was polled.</param>
public sealed record CoverageStats(int Total, int Fresh, int Never, DateTimeOffset? Oldest)
{
    public int FreshPercent => Total == 0 ? 0 : (int)Math.Floor(100.0 * Fresh / Total);
}
