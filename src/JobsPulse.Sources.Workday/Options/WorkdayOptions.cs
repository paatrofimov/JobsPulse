namespace JobsPulse.Sources.Workday.Options;

public sealed class WorkdayOptions
{
    public const string SectionName = "Sources:Workday";

    /// <summary>Ask the detail endpoint for every posting on every poll, as if a filter read descriptions.</summary>
    public bool IncludeContentOnPoll { get; set; }

    /// <summary>Detail requests per board traversal for new or changed postings; the rest are mapped list-only.</summary>
    public int MaxDetailsPerPoll { get; set; } = 100;

    public int DetailConcurrency { get; set; } = 4;

    /// <summary>Page size of the list endpoint. Workday rejects anything above 20 with HTTP 400.</summary>
    public int PageSize { get; set; } = 20;

    /// <summary>
    /// Safety cap on pagination. A page holds 20 vacancies, so the default covers a 5000-vacancy board; a board
    /// bigger than that is reported as an incomplete traversal and its state is left untouched.
    /// </summary>
    public int MaxPages { get; set; } = 250;

    public int RequestTimeoutSeconds { get; set; } = 30;
}
