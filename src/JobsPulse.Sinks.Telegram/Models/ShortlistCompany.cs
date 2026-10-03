using JobsPulse.Core.Model.Domain;

namespace JobsPulse.Sinks.Telegram.Models;

/// <summary>
/// One row of the shortlist: a company, the entry its link opens, how many of its vacancies are in the slice and
/// the best of them to apply to.
/// </summary>
public sealed record ShortlistCompany(
    string CompanyName,
    long EntryId,
    bool Worked,
    int Count,
    IReadOnlyList<Vacancy> Top);
