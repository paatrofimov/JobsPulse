namespace JobsPulse.Sources.Workday.Options;

public sealed class WorkdayOptions
{
    public const string SectionName = "Sources:Workday";

    /// <summary>Descriptions cost one request per vacancy, so they are off unless asked for.</summary>
    public bool IncludeContentOnPoll { get; set; }

    /// <summary>Page size of the list endpoint. Workday rejects anything above 20 with HTTP 400.</summary>
    public int PageSize { get; set; } = 20;

    /// <summary>
    /// Safety cap on pagination. A page holds 20 vacancies, so the default covers a 5000-vacancy board; a board
    /// bigger than that is reported as an incomplete traversal and its state is left untouched.
    /// </summary>
    public int MaxPages { get; set; } = 250;

    /// <summary>Budget of detail requests per board traversal - postings past it are mapped without a description.</summary>
    public int MaxDescriptionRequests { get; set; } = 100;

    public int RequestTimeoutSeconds { get; set; } = 30;
}
