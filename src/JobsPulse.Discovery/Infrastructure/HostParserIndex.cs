using JobsPulse.Core.Abstractions;
using JobsPulse.Discovery.Models;

namespace JobsPulse.Discovery.Infrastructure;

/// <summary>
/// Which ATS a host belongs to. The index returns millions of urls, so this is asked once per url and has to be a
/// lookup rather than a loop over the parsers: an exact host hits a dictionary, and only the few whole-domain targets
/// (an ATS that gives every tenant its own host) are walked as suffixes.
/// </summary>
public sealed class HostParserIndex
{
    private readonly Dictionary<string, IBoardUrlParser> byHost;
    private readonly List<(string Suffix, IBoardUrlParser Parser)> bySuffix;

    public HostParserIndex(
        IReadOnlyList<BoardIndexTarget> targets,
        Func<string, IBoardUrlParser> parserBySourceId)
    {
        byHost = new Dictionary<string, IBoardUrlParser>(StringComparer.OrdinalIgnoreCase);
        bySuffix = [];

        foreach (var target in targets)
        {
            var parser = parserBySourceId(target.SourceId);

            if (target.HostIsSuffix)
            {
                var suffix = '.' + target.Host;

                if (!bySuffix.Any(s => s.Suffix.Equals(suffix, StringComparison.OrdinalIgnoreCase)))
                    bySuffix.Add((suffix, parser));

                continue;
            }

            byHost[target.Host] = parser;
        }
    }

    public bool TryGet(string host, out IBoardUrlParser parser)
    {
        if (byHost.TryGetValue(host, out var exact))
        {
            parser = exact;
            return true;
        }

        foreach (var (suffix, owner) in bySuffix)
        {
            if (!host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                continue;

            parser = owner;
            return true;
        }

        parser = null!;
        return false;
    }
}
