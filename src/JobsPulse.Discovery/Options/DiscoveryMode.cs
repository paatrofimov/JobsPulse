namespace JobsPulse.Discovery.Options;

/// <summary>
/// Which crawl index readers a discovery run is allowed to use. Both may be on: the parquet pass runs first and
/// the http pass then picks up whatever is still not marked processed.
/// </summary>
[Flags]
public enum DiscoveryMode
{
    None = 0,

    /// <summary>The CDX http api of index.commoncrawl.org.</summary>
    Http = 1,

    /// <summary>The columnar index - remote parquet files read by DuckDB over http range requests.</summary>
    Parquet = 2
}
