namespace JobsPulse.Sources.HeadHunter.Models;

/// <summary>One employer of a search result together with how well its name answers what was asked for.</summary>
public sealed record HeadHunterEmployerMatch
{
    public required EmployerItemDto Employer { get; init; }

    /// <summary>0 - unrelated, 100 - the same name. See `HeadHunterEmployerMatcher` for what the values mean.</summary>
    public required int Score { get; init; }

    public int OpenVacancies => Employer.OpenVacancies ?? 0;
}
