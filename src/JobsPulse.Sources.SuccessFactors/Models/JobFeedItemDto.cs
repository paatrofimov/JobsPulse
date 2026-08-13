namespace JobsPulse.Sources.SuccessFactors.Models;

/// <summary>
/// One '&lt;item&gt;' of the career site feed. Every field is nullable: this is an unversioned feed generated for job
/// aggregators, not a contract, and a field that disappears must cost one field rather than the whole board.
/// <see cref="Id"/> is the only one an item cannot be mapped without.
/// </summary>
public sealed record JobFeedItemDto
{
    /// <summary>The recruiting marketing job id - stable per posting and the only identifier the feed carries.</summary>
    public string? Id { get; init; }

    /// <summary>Job title with the location appended in brackets - see the mapper for why it is taken off again.</summary>
    public string? Title { get; init; }

    /// <summary>Public job page on the career site.</summary>
    public string? Link { get; init; }

    /// <summary>Full description as html. Present in the feed whether it was asked for or not.</summary>
    public string? Description { get; init; }

    public string? Location { get; init; }

    public string? Employer { get; init; }

    public string? JobFunction { get; init; }

    /// <summary>
    /// When the posting expires. Deliberately not mapped to a date on the vacancy: it is not a publication date, and
    /// the sites refresh it on their own schedule.
    /// </summary>
    public string? ExpirationDate { get; init; }
}
