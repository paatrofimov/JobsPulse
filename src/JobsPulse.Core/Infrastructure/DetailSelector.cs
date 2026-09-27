using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Core.Pipeline;

namespace JobsPulse.Core.Infrastructure;

public static class DetailSelector
{
    /// <summary>
    /// Decides, per posting of a list-only mapping, whether the per-posting detail endpoint has to be asked.
    /// A posting no storage filter can accept by title is left list-only; a known posting whose list data did not
    /// move reuses the stored vacancy; the rest is fetched up to <paramref name="budget"/>, so a first sight of a big
    /// board still finishes inside the traversal timeout instead of failing on every cycle.
    ///
    /// A reused posting under a description filter comes back without a description, so the source marks it
    /// <see cref="Vacancy.DescriptionUnavailable"/> and it keeps the verdict its description got when it was first
    /// read. Fetching the description of every stored posting on every cycle, only to confirm that verdict, was
    /// thousands of detail requests per polling cycle.
    /// </summary>
    public static DetailDecision[] Select(
        IReadOnlyList<Vacancy> listed,
        SourceTarget target,
        bool fetchAll,
        int budget,
        Func<Vacancy, Vacancy, bool> listUnchanged)
    {
        var decisions = new DetailDecision[listed.Count];

        if (fetchAll)
        {
            Array.Fill(decisions, DetailDecision.Fetch);
            return decisions;
        }

        for (var i = 0; i < listed.Count; i++)
        {
            var vacancy = listed[i];

            if (target.MayBeStored?.Invoke(vacancy) == false)
                decisions[i] = DetailDecision.ListOnly;
            // Rejected with the very same list data under the same filters - the detail would say the same again.
            else if (target.Rejected.TryGetValue(vacancy.PostId, out var rejected) && rejected == ListHash(vacancy))
                decisions[i] = DetailDecision.Rejected;
            else if (target.Known.TryGetValue(vacancy.PostId, out var known)
                     && listUnchanged(vacancy, known)
                     && VerdictHolds(known, target))
                decisions[i] = DetailDecision.Reuse;
            // Descriptions are not stored, so a description filter needs one for every new or changed plausible
            // posting - and without a budget: a posting left list-only could never pass the description rule.
            else if (target.NeedsDescription)
                decisions[i] = DetailDecision.Fetch;
            else if (budget-- > 0)
                decisions[i] = DetailDecision.Fetch;
            else
                decisions[i] = DetailDecision.ListOnly;
        }

        return decisions;
    }

    /// <summary>
    /// Whether the description verdict of a known posting still stands: no description rule is in force, or it was
    /// given under the current rules. A row stored before the rules were tracked (no hash) is taken as current - it
    /// was re-read on every poll back then, so its verdict is the one the rules in force gave.
    /// </summary>
    private static bool VerdictHolds(Vacancy known, SourceTarget target) =>
        !target.NeedsDescription
        || known.DescriptionRulesHash is null
        || string.Equals(known.DescriptionRulesHash, target.DescriptionRulesHash, StringComparison.Ordinal);

    /// <summary>Fingerprint of a list-only mapping: its hashed fields, all of which come from the list there.</summary>
    public static string ListHash(Vacancy listed) => VacancyHasher.Compute(listed);

    /// <summary>A vacancy mapped with its detail, carrying the fingerprint a rejection is remembered by.</summary>
    public static Vacancy Detailed(Vacancy vacancy, Vacancy listed) => vacancy with { ListHash = ListHash(listed) };

    /// <summary>A posting skipped as known rejected - list-only, and never stored or matched.</summary>
    public static Vacancy Rejected(Vacancy listed) => listed with { ListHash = ListHash(listed), KnownRejected = true };

    /// <summary>Runs <paramref name="fetch"/> for every posting marked <see cref="DetailDecision.Fetch"/>.</summary>
    public static async Task<TDetail?[]> FetchAsync<TDetail>(
        IReadOnlyList<DetailDecision> decisions,
        int concurrency,
        Func<int, CancellationToken, Task<TDetail?>> fetch,
        CancellationToken ct)
        where TDetail : class
    {
        var details = new TDetail?[decisions.Count];
        var indices = Enumerable.Range(0, decisions.Count).Where(i => decisions[i] == DetailDecision.Fetch);

        await Parallel.ForEachAsync(
            indices,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, concurrency), CancellationToken = ct },
            async (i, token) => details[i] = await fetch(i, token));

        return details;
    }
}
