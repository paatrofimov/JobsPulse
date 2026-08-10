using System.Collections.Concurrent;
using JobsPulse.Sources.Lever.Models;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Sources.Lever.Infrastructure;

/// <summary>
/// Which Lever instance a site lives on. Discovered once by probing and then remembered, so paging a board and
/// polling it again cost no extra requests. Singleton and in-memory only: the mapping is not worth a schema of its
/// own, and after a restart it is re-probed once per site.
/// </summary>
public sealed class LeverRegionMap(ILog log)
{
    private readonly ILog ctxLog = log.ForContext<LeverRegionMap>();

    private readonly ConcurrentDictionary<string, LeverRegion> bySite = new(StringComparer.OrdinalIgnoreCase);

    public bool TryGet(string site, out LeverRegion region) => bySite.TryGetValue(site, out region!);

    public void Set(string site, LeverRegion region)
    {
        if (bySite.TryAdd(site, region))
            ctxLog.Info("Lever site '{Site}' lives on the {Region} instance", site, region.Id);
        else
            bySite[site] = region;
    }
}
