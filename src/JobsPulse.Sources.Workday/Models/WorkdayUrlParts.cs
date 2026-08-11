namespace JobsPulse.Sources.Workday.Models;

/// <summary>
/// What a public Workday url tells us on its own. The tenant is only ever a hint here - it has to be confirmed
/// against the careers site or the backend before it becomes a board configuration.
/// </summary>
public sealed record WorkdayUrlParts
{
    public required string Host { get; init; }

    public required string Site { get; init; }

    public string? TenantHint { get; init; }

    public WorkdayHostKind Kind { get; init; }

    /// <summary>True for a board url, false for a deep link to a single vacancy.</summary>
    public bool IsBoardRoot { get; init; }

    /// <summary>Careers site url of the board itself, with the locale and any deep path dropped.</summary>
    public string BoardUrl => Kind == WorkdayHostKind.MyWorkdaySite
        ? $"https://{Host}/recruiting/{TenantHint}/{Site}"
        : $"https://{Host}/{Site}";
}
