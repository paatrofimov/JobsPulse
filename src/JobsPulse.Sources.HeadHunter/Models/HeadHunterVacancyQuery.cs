namespace JobsPulse.Sources.HeadHunter.Models;

/// <summary>
/// One page of the common vacancy search, narrowed to one employer. The dates are how paging gets past the api's
/// `page` * `per_page` ceiling - see `HeadHunterBoardSource`.
/// </summary>
public sealed record HeadHunterVacancyQuery
{
    public required string EmployerId { get; init; }

    public int Page { get; init; }

    public int PerPage { get; init; }

    /// <summary>Upper bound on the publication date - the window the next batch of pages is read from.</summary>
    public DateTimeOffset? DateTo { get; init; }

    public string? OrderBy { get; init; }
}
