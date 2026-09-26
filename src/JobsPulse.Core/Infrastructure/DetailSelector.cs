using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Core.Infrastructure;

public static class DetailSelector
{
    /// <summary>
    /// Decides, per posting of a list-only mapping, whether the per-posting detail endpoint has to be asked.
    /// A posting no storage filter can accept by title is left list-only; a known posting whose list data did not
    /// move reuses the stored vacancy; the rest is fetched up to <paramref name="budget"/>, so a first sight of a big
    /// board still finishes inside the traversal timeout instead of failing on every cycle.
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
            // Descriptions are not stored, so a description filter needs a fresh one for every plausible posting -
            // and without a budget: a stored vacancy mapped without its description would fail the filter and close.
            else if (target.NeedsDescription)
                decisions[i] = DetailDecision.Fetch;
            else if (target.Known.TryGetValue(vacancy.PostId, out var known) && listUnchanged(vacancy, known))
                decisions[i] = DetailDecision.Reuse;
            else if (budget-- > 0)
                decisions[i] = DetailDecision.Fetch;
            else
                decisions[i] = DetailDecision.ListOnly;
        }

        return decisions;
    }

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
