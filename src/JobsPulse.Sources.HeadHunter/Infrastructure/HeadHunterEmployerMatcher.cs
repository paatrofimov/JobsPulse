using JobsPulse.Sources.HeadHunter.Models;

namespace JobsPulse.Sources.HeadHunter.Infrastructure;

/// <summary>
/// Ranks the employers a catalog search answered with. This exists because the search is fuzzy by design - it is what
/// matches 'Yandex' to 'Яндекс' and tolerates a typo - and pays for that with a tail of employers that merely share a
/// word with the query. Exact-name matching cannot be the rule either: the catalog spells companies out in full
/// ('ООО "Яндекс.Такси"'), one company is often several employer records (a group, its regions, its brands), and the
/// name a user types is a brand rather than a legal entity.
///
/// So the answer is a score and an order, not a decision. The thresholds - what is plausible at all, and how far ahead
/// the leader has to be to answer on its own - live in `HeadHunterBoardResolver`, which is also what turns an ambiguous
/// result into a choice for the user instead of a guess.
/// </summary>
public static class HeadHunterEmployerMatcher
{
    /// <summary>Same name once the legal form and the punctuation are gone.</summary>
    public const int ExactScore = 100;

    private const int PrefixScore = 85;
    private const int ContainedScore = 75;
    private const int SubstringScore = 60;

    public static IReadOnlyList<HeadHunterEmployerMatch> Rank(
        string companyName,
        IEnumerable<EmployerItemDto> employers)
    {
        var normalized = HeadHunterCompanyName.Normalize(companyName);
        var tokens = HeadHunterCompanyName.Tokens(normalized);
        var compact = HeadHunterCompanyName.Compact(tokens);

        return employers
            .Where(e => !string.IsNullOrWhiteSpace(e.Id) && !string.IsNullOrWhiteSpace(e.Name))
            .Select(e => new HeadHunterEmployerMatch
            {
                Employer = e,
                Score = Score(tokens, compact, e.Name!)
            })
            // An employer with more open vacancies is the parent record of a group far more often than not, so it is
            // the better answer among names that score the same.
            .OrderByDescending(m => m.Score)
            .ThenByDescending(m => m.OpenVacancies)
            .ThenBy(m => m.Employer.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static int Score(string companyName, string employerName)
    {
        var normalized = HeadHunterCompanyName.Normalize(companyName);
        var tokens = HeadHunterCompanyName.Tokens(normalized);

        return Score(tokens, HeadHunterCompanyName.Compact(tokens), employerName);
    }

    private static int Score(IReadOnlyList<string> queryTokens, string queryCompact, string employerName)
    {
        var employerTokens = HeadHunterCompanyName.Tokens(HeadHunterCompanyName.Normalize(employerName));
        var employerCompact = HeadHunterCompanyName.Compact(employerTokens);

        if (queryCompact.Length == 0 || employerCompact.Length == 0)
            return 0;

        if (string.Equals(queryCompact, employerCompact, StringComparison.Ordinal))
            return ExactScore;

        // 'yandex' asked for, 'yandex taxi' found - the brand is what a user types, so this is a strong match.
        if (queryCompact.Length >= 3 && employerCompact.StartsWith(queryCompact, StringComparison.Ordinal))
            return PrefixScore;

        // The other way round is weaker: the user named something more specific than the employer record is.
        if (employerCompact.Length >= 3 && queryCompact.StartsWith(employerCompact, StringComparison.Ordinal))
            return ContainedScore;

        var shared = employerTokens.Distinct(StringComparer.Ordinal).Count(queryTokens.Contains);

        if (shared == 0)
        {
            return queryCompact.Length >= 4 && employerCompact.Contains(queryCompact, StringComparison.Ordinal)
                ? SubstringScore
                : 0;
        }

        // Two ratios, because both failure modes matter: a name missing half of what was asked for is a different
        // company, and a name burying the query among four other words usually is too.
        var coverage = shared / (double)queryTokens.Count;
        var focus = shared / (double)employerTokens.Count;

        return (int)Math.Round(65 * coverage + 30 * focus);
    }
}
