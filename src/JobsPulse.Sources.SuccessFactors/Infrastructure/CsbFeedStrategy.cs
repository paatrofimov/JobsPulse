using JobsPulse.Core.Model.Domain;
using JobsPulse.Sources.SuccessFactors.Abstractions;
using JobsPulse.Sources.SuccessFactors.Models;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Sources.SuccessFactors.Infrastructure;

/// <summary>
/// The primary strategy, and the reason this source is cheap: one request returns the whole Career Site Builder
/// board - every vacancy, with its description. There is no pagination to walk and no per-vacancy request to budget,
/// which is the opposite of every other ATS here, where a description costs one request each.
///
/// It applies to any board that has a branded domain, whatever generation the site is otherwise: the feed servlet is
/// part of the recruiting marketing platform underneath, not of the page templates on top of it.
/// </summary>
public sealed class CsbFeedStrategy(
    SuccessFactorsFeedClient client,
    SuccessFactorsMapper mapper,
    ILog log) : ISuccessFactorsListStrategy
{
    private readonly ILog ctxLog = log.ForContext<CsbFeedStrategy>();

    public string Name => "feed";

    public int Priority => 0;

    public bool CanServe(SuccessFactorsBoardConfig config) => config.HasDomain;

    public async Task<SuccessFactorsFetch<SuccessFactorsListing>> FetchAsync(
        SuccessFactorsBoardConfig config,
        bool includeDescriptions,
        CancellationToken ct)
    {
        var response = await client.GetFeedAsync(config, includeDescriptions, ct);

        if (!response.Success)
        {
            return new SuccessFactorsFetch<SuccessFactorsListing>(
                null, response.NotFound, response.Truncated, response.Error);
        }

        var feed = response.Value!;
        var vacancies = new List<Vacancy>(feed.Items.Count);
        var skipped = 0;

        foreach (var item in feed.Items)
        {
            var vacancy = mapper.ToVacancy(item, config);

            if (vacancy is null)
                skipped++;
            else
                vacancies.Add(vacancy);
        }

        if (skipped > 0)
        {
            ctxLog.Debug(
                "Board {Board}: {Skipped} feed items carry no id and are dropped",
                config.BoardId, skipped);
        }

        return SuccessFactorsFetch<SuccessFactorsListing>.Ok(new SuccessFactorsListing
        {
            Vacancies = vacancies,
            Strategy = Name,
            DisplayName = feed.Title,
            Locale = feed.Language
        });
    }
}
