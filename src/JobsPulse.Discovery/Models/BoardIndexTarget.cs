namespace JobsPulse.Discovery.Models;

/// <summary>
/// One `IBoardUrlParser` url pattern translated into something the columnar index can be asked about: the tld, the
/// host and the path prefix. The tld and the path are plain equality, so no public suffix or surt canonicalization
/// assumption can silently drop a board; the host is equality too unless the pattern asked for a whole domain -
/// see <see cref="HostIsSuffix"/>.
/// </summary>
public sealed record BoardIndexTarget
{
    public required string SourceId { get; init; }

    /// <summary>Last host label - the one column of the index that carries usable row group statistics.</summary>
    public required string Tld { get; init; }

    /// <summary>The exact host, or the domain every board host ends with when <see cref="HostIsSuffix"/>.</summary>
    public required string Host { get; init; }

    /// <summary>
    /// The pattern was '*.myworkdayjobs.com/*': the board host is per company, so the index has to be asked for every
    /// subdomain of the domain instead of one known host. Costlier than equality, and the only way to reach an ATS
    /// that gives each tenant its own host.
    /// </summary>
    public bool HostIsSuffix { get; init; }

    /// <summary>Always starts with '/'; '/' means the whole host.</summary>
    public required string PathPrefix { get; init; }

    /// <summary>Whether a host seen in the index belongs to this target.</summary>
    public bool Matches(string host) =>
        HostIsSuffix
            ? host.EndsWith('.' + Host, StringComparison.OrdinalIgnoreCase)
            : string.Equals(host, Host, StringComparison.OrdinalIgnoreCase);

    /// <summary>How the host reads in a log line: 'boards.greenhouse.io' or '*.myworkdayjobs.com'.</summary>
    public string HostLabel => HostIsSuffix ? "*." + Host : Host;
}
