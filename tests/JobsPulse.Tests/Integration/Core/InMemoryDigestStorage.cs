using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Tests.Integration.Core;

/// <summary>`watchlist_digest` in a list - the digest chain is what the tests check, a fake would only restate it.</summary>
internal sealed class InMemoryDigestStorage : IWatchlistDigestStorage
{
    public List<WatchlistDigest> Rows { get; } = [];

    public Task<WatchlistDigest?> GetAsync(long id, CancellationToken ct) =>
        Task.FromResult(Rows.FirstOrDefault(x => x.Id == id));

    public Task<WatchlistDigest?> GetLastDeliveredAsync(long watchlistId, CancellationToken ct) =>
        Task.FromResult(Rows
            .Where(x => x.WatchlistId == watchlistId && x.DeliveredAt is not null)
            .MaxBy(x => x.To));

    public Task<WatchlistDigest> AddAsync(
        long watchlistId,
        DateTimeOffset from,
        DateTimeOffset to,
        long? previousId,
        CancellationToken ct)
    {
        var row = new WatchlistDigest
        {
            Id = Rows.Count + 1,
            WatchlistId = watchlistId,
            From = from,
            To = to,
            PreviousId = previousId
        };

        Rows.Add(row);

        return Task.FromResult(row);
    }

    public Task MarkDeliveredAsync(long id, DateTimeOffset deliveredAt, CancellationToken ct)
    {
        var i = Rows.FindIndex(x => x.Id == id);
        Rows[i] = Rows[i] with { DeliveredAt = deliveredAt };

        return Task.CompletedTask;
    }
}
