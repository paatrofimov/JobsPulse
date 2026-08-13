namespace JobsPulse.Sources.HeadHunter.Models;

/// <summary>
/// What a HeadHunter url addresses: an employer, or one vacancy of an employer that is not named in the url. Exactly
/// one of the two ids is set.
/// </summary>
public sealed record HeadHunterUrlParts
{
    public string? EmployerId { get; init; }

    public string? VacancyId { get; init; }

    /// <summary>The regional host the link was written for - 'hh.ru', 'hh.kz', 'rabota.by'.</summary>
    public required string Host { get; init; }
}
