using JobsPulse.Discovery.Models;

namespace JobsPulse.Discovery.Abstractions;

/// <summary>
/// Reads the columnar crawl index straight from the remote parquet files - nothing is downloaded or cached locally.
/// </summary>
public interface IParquetIndexClient
{
    /// <summary>
    /// Narrows a set of parquet files to the ones that hold anything for the probe, reading only the narrow columns.
    /// </summary>
    /// <exception cref="ParquetIndexUnavailableException">Every attempt has failed.</exception>
    Task<IReadOnlyList<string>> ProbeFilesAsync(ParquetFileProbe probe, CancellationToken ct);

    /// <summary>
    /// Runs one query and hands every distinct board url to <paramref name="onUrl"/> as it arrives. Returns the
    /// number of rows the index answered with.
    /// </summary>
    /// <exception cref="ParquetIndexUnavailableException">Every attempt has failed.</exception>
    Task<long> ScanAsync(ParquetIndexQuery query, Action<string> onUrl, CancellationToken ct);
}
