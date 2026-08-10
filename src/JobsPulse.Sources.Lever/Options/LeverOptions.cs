namespace JobsPulse.Sources.Lever.Options;

public sealed class LeverOptions
{
    public const string SectionName = "Sources:Lever";
    
    public bool IncludeContentOnPoll { get; set; }

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
