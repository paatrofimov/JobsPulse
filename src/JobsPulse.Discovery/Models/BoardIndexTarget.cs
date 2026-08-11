namespace JobsPulse.Discovery.Models;

/// <summary>
/// One `IBoardUrlParser` url pattern translated into something the columnar index can be asked about: the tld, the
/// exact host and the path prefix. All three are plain equality, so no public suffix or surt canonicalization
/// assumption can silently drop a board.
/// </summary>
public sealed record BoardIndexTarget
{
    public required string SourceId { get; init; }

    /// <summary>Last host label - the one column of the index that carries usable row group statistics.</summary>
    public required string Tld { get; init; }

    public required string Host { get; init; }

    /// <summary>Always starts with '/'; '/' means the whole host.</summary>
    public required string PathPrefix { get; init; }
}
