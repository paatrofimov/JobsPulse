using JobsPulse.Core.Model.Domain;

namespace JobsPulse.Sources.SuccessFactors.Models;

/// <summary>
/// What a strategy answers with: the vacancies of one board plus what the site said about itself while handing them
/// over. <see cref="DisplayName"/> and <see cref="Locale"/> exist because the resolver needs a company name and the
/// site is the only one who knows it - the tenant id is not a name and a domain is not either.
/// </summary>
public sealed record SuccessFactorsListing
{
    public required IReadOnlyList<Vacancy> Vacancies { get; init; }

    /// <summary>Which strategy produced this, for the log line - the strategies differ in data quality.</summary>
    public required string Strategy { get; init; }

    public string? DisplayName { get; init; }

    public string? Locale { get; init; }
}
