namespace JobsPulse.Discovery.Models;

/// <summary>
/// A file selection step: which of these parquet files hold anything at all for these targets. Answering it costs
/// one narrow column, while the query that follows costs the wide ones - which is the whole point of asking first.
/// </summary>
public sealed record ParquetFileProbe
{
    public required IReadOnlyList<string> Files { get; init; }

    public required IReadOnlyList<string> Tlds { get; init; }

    /// <summary>Null - the tld alone is asked about, which is the cheap first step.</summary>
    public IReadOnlyList<string>? Hosts { get; init; }

    /// <summary>
    /// Domains whose every subdomain is a board host - see <see cref="BoardIndexTarget.HostIsSuffix"/>. Asked with a
    /// suffix match instead of equality, so they are listed separately from <see cref="Hosts"/>.
    /// </summary>
    public IReadOnlyList<string>? HostSuffixes { get; init; }

    public int FetchStatus { get; init; } = 200;
}
