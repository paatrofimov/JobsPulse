namespace JobsPulse.Discovery.Models;

/// <summary>
/// A parquet index query never succeeded, so the caller has no data - not an empty answer, but no answer at all.
/// The twin of <see cref="CrawlIndexUnavailableException"/>: the collection must stay pending.
/// </summary>
public sealed class ParquetIndexUnavailableException(int files, int attempts, string failure, Exception? inner = null)
    : Exception($"Parquet index query over {files} file(s) has failed after {attempts} attempt(s) ({failure})", inner)
{
    public int Files { get; } = files;

    public int Attempts { get; } = attempts;

    public string Failure { get; } = failure;
}
