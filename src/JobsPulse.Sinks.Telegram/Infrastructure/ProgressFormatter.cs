using System.Text;
using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Sinks.Telegram.Infrastructure;

/// <summary>
/// The traversal progress as one html block: what the two cycles are doing right now, and how much of each dataset -
/// watchlist boards, the registry, the crawl indexes - has been walked, per source and in total. Pure and static:
/// the admin screen and the <c>/progress</c> command render the very same text.
///
/// English only, like the rest of the operator surface.
/// </summary>
public static class ProgressFormatter
{
    public static string Render(
        IReadOnlyList<TraversalProgress> traversals,
        DiscoveryProgress discovery,
        IReadOnlyDictionary<string, int> registryBySource,
        DateTimeOffset now)
    {
        var sb = new StringBuilder("<h6>🛰 Traversal progress</h6>");

        foreach (var traversal in traversals)
        {
            sb.Append(traversal.Kind switch
            {
                TraversalKind.Watchlist => RenderTraversal("Watchlist boards", traversal, now, null),
                TraversalKind.Registry => RenderTraversal("Registry sweep", traversal, now, registryBySource),
                _ => string.Empty
            });
        }

        sb.Append(RenderDiscovery(discovery));

        return sb.ToString();
    }

    private static string RenderTraversal(
        string title,
        TraversalProgress traversal,
        DateTimeOffset now,
        IReadOnlyDictionary<string, int>? registryBySource)
    {
        var sb = new StringBuilder($"<p><b>{title}</b> — {State(traversal, now)}<br>");

        if (!traversal.HasRun)
            return sb.Append("</p>").ToString();

        sb.Append($"cycle: <b>{traversal.Done}</b> of {traversal.Planned} ({traversal.CyclePercent}%)");

        if (traversal.Failed > 0)
            sb.Append($", errors <b>{traversal.Failed}</b>");

        sb.Append($"<br>dataset: <b>{traversal.DatasetCovered}</b> of {traversal.DatasetTotal} boards "
                  + $"({traversal.DatasetPercent}%)<br>");

        foreach (var source in traversal.Sources)
        {
            sb.Append($"• <code>{MessageFormatter.Escape(source.SourceId)}</code>: "
                      + $"<b>{source.DatasetCovered}</b> of {source.DatasetTotal} ({source.DatasetPercent}%)"
                      + $", cycle {source.Done}/{source.Planned}");

            // The registry holds inactive and already watched boards too, so its row count is a separate number.
            if (registryBySource?.GetValueOrDefault(source.SourceId) is { } known and > 0)
                sb.Append($", registry {known}");

            sb.Append("<br>");
        }

        // A source that has never been reached by a cycle exists only in the registry - say so instead of hiding it.
        List<KeyValuePair<string, int>> untouched = registryBySource is null
            ?
            []
            :
            [
                .. registryBySource
                    .Where(pair => traversal.Sources.All(
                        s => !string.Equals(s.SourceId, pair.Key, StringComparison.OrdinalIgnoreCase)))
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            ];

        foreach (var (sourceId, known) in untouched)
        {
            sb.Append($"• <code>{MessageFormatter.Escape(sourceId)}</code>: not swept yet, registry {known}<br>");
        }

        return sb.Append("</p>").ToString();
    }

    private static string RenderDiscovery(DiscoveryProgress discovery)
    {
        var sb = new StringBuilder(
            $"<p><b>Crawl indexes</b> — {(discovery.IsRunning ? "mining now" : "idle")}<br>");

        if (discovery.ProcessedBySource.Count == 0)
        {
            return sb.Append("nothing mined yet — /discover to walk them.</p>").ToString();
        }

        var total = discovery.CollectionsTotal;

        sb.Append(total > 0
            ? $"published indexes: <b>{total}</b><br>"
            : "published index count is unavailable<br>");

        foreach (var (sourceId, processed) in discovery.ProcessedBySource.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            sb.Append($"• <code>{MessageFormatter.Escape(sourceId)}</code>: <b>{processed}</b>");

            if (total > 0)
                sb.Append($" of {total} ({TraversalProgress.Percent(processed, total)}%)");

            sb.Append("<br>");
        }

        return sb.Append("</p>").ToString();
    }

    /// <summary>«running for 2m», «finished 3m ago», or «never run» - the first thing an operator looks at.</summary>
    private static string State(TraversalProgress traversal, DateTimeOffset now)
    {
        if (!traversal.HasRun)
            return "not started yet";

        if (traversal.IsRunning)
            return $"running for {Elapsed(now - traversal.StartedAt!.Value)}";

        return traversal.FinishedAt is { } finished
            ? $"idle, last cycle finished {Elapsed(now - finished)} ago"
            : "idle";
    }

    private static string Elapsed(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
            span = TimeSpan.Zero;

        if (span.TotalMinutes < 1)
            return $"{span.Seconds}s";

        return span.TotalHours < 1
            ? $"{(int)span.TotalMinutes}m {span.Seconds}s"
            : $"{(int)span.TotalHours}h {span.Minutes}m";
    }
}
