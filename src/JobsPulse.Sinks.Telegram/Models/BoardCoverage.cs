namespace JobsPulse.Sinks.Telegram.Models;

public sealed record BoardCoverage(
    DateTimeOffset FreshSince,
    CoverageStats Total,
    IReadOnlyList<(string SourceId, CoverageStats Stats)> BySource)
{
    public static readonly BoardCoverage Empty = new(DateTimeOffset.MinValue, new CoverageStats(0, 0, 0, null), []);

    /// <param name="boards">The set, as <c>(sourceId, "{sourceId}/{boardId}")</c> pairs.</param>
    /// <param name="stamps">Last poll per board key, as <c>IBoardPollStateStorage.LoadAsync</c> returns it.</param>
    public static BoardCoverage Compute(
        IReadOnlyCollection<(string SourceId, string BoardKey)> boards,
        IReadOnlyDictionary<string, DateTimeOffset> stamps,
        DateTimeOffset freshSince)
    {
        return new BoardCoverage(
            freshSince,
            Stats(boards),
            [
                .. boards
                    .GroupBy(b => b.SourceId, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(g => g.Key, StringComparer.Ordinal)
                    .Select(g => (g.Key, Stats(g.ToList())))
            ]);

        CoverageStats Stats(IReadOnlyCollection<(string SourceId, string BoardKey)> set)
        {
            var fresh = 0;
            var never = 0;
            DateTimeOffset? oldest = null;

            foreach (var (_, key) in set)
            {
                if (!stamps.TryGetValue(key, out var polled))
                {
                    never++;
                    continue;
                }

                if (polled >= freshSince)
                    fresh++;

                if (oldest is null || polled < oldest)
                    oldest = polled;
            }

            return new CoverageStats(set.Count, fresh, never, oldest);
        }
    }
}
