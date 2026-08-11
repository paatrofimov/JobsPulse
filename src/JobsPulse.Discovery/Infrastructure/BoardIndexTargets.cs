using JobsPulse.Core.Abstractions;
using JobsPulse.Discovery.Models;

namespace JobsPulse.Discovery.Infrastructure;

/// <summary>
/// Turns the cdx url patterns a source already declares ('boards-api.greenhouse.io/v1/boards/*') into columnar index
/// targets, so adding an ATS still means adding one <see cref="IBoardUrlParser"/> and nothing else.
/// </summary>
public static class BoardIndexTargets
{
    private static readonly string[] Schemes = ["https://", "http://", "*://"];

    public static IReadOnlyList<BoardIndexTarget> From(IEnumerable<IBoardUrlParser> parsers) =>
        parsers
            .SelectMany(p => p.IndexUrlPatterns.Select(pattern => From(p.SourceId, pattern)))
            .Where(t => t is not null)
            .Select(t => t!)
            .DistinctBy(t => (t.SourceId, t.Host, t.PathPrefix))
            .ToList();

    private static BoardIndexTarget? From(string sourceId, string pattern)
    {
        var cleaned = pattern.Trim();

        foreach (var scheme in Schemes)
        {
            if (cleaned.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
                cleaned = cleaned[scheme.Length..];
        }

        // The cdx syntax is 'host/path/*' - everything from the wildcard on is the part we are looking for.
        var wildcard = cleaned.IndexOf('*');
        if (wildcard >= 0)
            cleaned = cleaned[..wildcard];

        var slash = cleaned.IndexOf('/');
        var host = (slash < 0 ? cleaned : cleaned[..slash]).Trim().ToLowerInvariant();
        if (host.Length == 0 || host.Contains('*'))
            return null;

        var dot = host.LastIndexOf('.');
        if (dot <= 0 || dot == host.Length - 1)
            return null;

        var path = slash < 0 ? "/" : cleaned[slash..];
        if (path.Length == 0)
            path = "/";

        return new BoardIndexTarget
        {
            SourceId = sourceId,
            Tld = host[(dot + 1)..],
            Host = host,
            PathPrefix = path
        };
    }
}
