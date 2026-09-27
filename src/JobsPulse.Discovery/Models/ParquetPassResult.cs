using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Discovery.Models;

/// <summary>
/// What the columnar pass did, plus the collections the http index should be asked about instead: the ones that
/// failed and the ones left when the columnar index looked unavailable. Collections left by the token cap are not
/// among them - the http index would only hit the same cap, far more slowly.
/// </summary>
public sealed record ParquetPassResult(BoardDiscoveryReport Report, IReadOnlyList<CrawlCollection> FallbackCollections)
{
    public static readonly ParquetPassResult Empty = new(BoardDiscoveryReport.Empty, []);
}
