namespace JobsPulse.Core.Abstractions;

/// <summary>
/// Per-ATS knowledge for crawl index mining: which URLs to ask the index for and how to read a board id out of one.
/// Implemented in the source project (Greenhouse, Lever, ...), consumed by the generic discovery pipeline.
/// </summary>
public interface IBoardUrlParser
{
    string SourceId { get; }

    /// <summary>Crawl index url patterns, e.g. 'boards.greenhouse.io/*'.</summary>
    IReadOnlyList<string> IndexUrlPatterns { get; }

    bool TryParseBoardId(string url, out string boardId);
}
