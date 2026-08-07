namespace JobsPulse.Core.Model.Infrastructure;

public sealed record Watchlist
{
    public int Version { get; init; }

    /// Applied to entries without custom filter
    public FilterSpec DefaultFilter { get; init; } = new();

    public IReadOnlyList<WatchEntry> Entries { get; init; } = [];

    public WatchEntry? Find(string idOrName) =>
        Entries.FirstOrDefault(e =>
            string.Equals(e.Id, idOrName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(e.CompanyName, idOrName, StringComparison.OrdinalIgnoreCase));
}