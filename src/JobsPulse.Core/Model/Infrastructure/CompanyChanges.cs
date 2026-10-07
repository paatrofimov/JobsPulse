namespace JobsPulse.Core.Model.Infrastructure;

/// <summary>What one company's matching vacancies did during a period - one row of a digest's «all changes».</summary>
public sealed record CompanyChanges
{
    public required string CompanyName { get; init; }

    /// <summary>Vacancies that started matching.</summary>
    public required int Opened { get; init; }

    /// <summary>Matching vacancies that left their board.</summary>
    public required int Closed { get; init; }

    /// <summary>Matching vacancies that aged out or stopped passing the filter while still on the board.</summary>
    public required int Dropped { get; init; }

    /// <summary>The open count moved by the period: <see cref="Opened"/> minus everything that ended.</summary>
    public int Net => Opened - Closed - Dropped;
}
