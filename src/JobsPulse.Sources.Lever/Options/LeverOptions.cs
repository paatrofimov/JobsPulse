namespace JobsPulse.Sources.Lever.Options;

public sealed class LeverOptions
{
    public const string SectionName = "Sources:Lever";

    public bool IncludeContentOnPoll { get; set; }

    /// <summary>
    /// Lever instances to look a site up on, in order: `global`, `eu`. A site lives on exactly one of them, so the
    /// order only decides which one is asked first. An empty or unknown list falls back to all known instances.
    /// </summary>
    public IReadOnlyList<string> Regions { get; set; } = ["global", "eu"];

    /// <summary>Page size of the postings API (`limit`). Lever caps it at 100.</summary>
    public int PageSize { get; set; } = 100;

    /// <summary>Safety cap on pagination - a board bigger than this is reported as an incomplete traversal.</summary>
    public int MaxPages { get; set; } = 50;

    /// <summary>How many postings a probe reads to report the board size.</summary>
    public int ProbePageSize { get; set; } = 100;

    public int MaxSlugGuesses { get; set; } = 8;

    // Server-side filters of the postings API - unlike Greenhouse, Lever can narrow the board for us.
    public IReadOnlyList<string> Location { get; set; } = [];
    public IReadOnlyList<string> Team { get; set; } = [];
    public IReadOnlyList<string> Department { get; set; } = [];
    public IReadOnlyList<string> Commitment { get; set; } = [];
    public IReadOnlyList<string> Level { get; set; } = [];
}
