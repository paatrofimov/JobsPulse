using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Core.Abstractions;

/// <summary>Delivers statistics and run reports. A watchlist's report goes wherever its notifications go.</summary>
public interface IReportSink
{
    /// <summary>The scheduled statistics of a watchlist for a period.</summary>
    Task<DeliveryResult> DeliverDigestAsync(Watchlist watchlist, WatchlistStats stats, CancellationToken ct);

    /// <summary>What one polling or registry run changed in a watchlist.</summary>
    Task<DeliveryResult> DeliverRunAsync(Watchlist watchlist, TraversalRunReport report, CancellationToken ct);

    /// <summary>What one discovery run found - not bound to a watchlist, so it goes to the default chat.</summary>
    Task<DeliveryResult> DeliverDiscoveryAsync(DiscoveryRunReport report, CancellationToken ct);
}
