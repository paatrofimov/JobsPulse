namespace JobsPulse.Sources.SmartRecruiters.Options;

public sealed class SmartRecruitersOptions
{
    public const string SectionName = "Sources:SmartRecruiters";

    public string BaseUrl { get; set; } = "https://api.smartrecruiters.com/v1/companies/";

    /// <summary>Descriptions cost one extra request per posting - the list endpoint carries none.</summary>
    public bool IncludeContentOnPoll { get; set; }

    /// <summary>Page size of the posting API (`limit`). SmartRecruiters caps it at 100.</summary>
    public int PageSize { get; set; } = 100;

    /// <summary>Safety cap on pagination - a board bigger than this is reported as an incomplete traversal.</summary>
    public int MaxPages { get; set; } = 50;

    /// <summary>A probe only needs `totalFound`, so one posting is enough to prove the company exists.</summary>
    public int ProbePageSize { get; set; } = 1;

    public int MaxSlugGuesses { get; set; } = 8;

    /// <summary>Upper bound on description requests per traversal - the rest of the board is left without them.</summary>
    public int MaxDescriptionRequests { get; set; } = 100;

    // Server-side filters of the posting API - single values, the API does not OR repeated keys.
    public string? Query { get; set; }
    public string? Country { get; set; }
    public string? Region { get; set; }
    public string? City { get; set; }
    public string? Department { get; set; }
    public string? Language { get; set; }
}
