using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Core.Abstractions;

public interface IBoardDiscoveryService
{
    /// <summary>
    /// Walks crawl indexes and fills the board registry. <paramref name="full"/> re-walks the whole bootstrap
    /// window ignoring already processed indexes; otherwise only unprocessed ones are read.
    /// Returns <see cref="BoardDiscoveryReport.Busy"/> when a run is already in progress.
    /// </summary>
    Task<BoardDiscoveryReport> RunAsync(bool full, CancellationToken ct);
}
