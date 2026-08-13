using JobsPulse.Core.Model.Domain;
using JobsPulse.Sources.SuccessFactors.Abstractions;
using JobsPulse.Sources.SuccessFactors.Models;
using JobsPulse.Sources.SuccessFactors.Options;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Sources.SuccessFactors.Infrastructure;

/// <summary>
/// The fallback for a career site builder board the feed cannot serve: one whose feed is bigger than the byte budget,
/// or one the site cuts off itself - both real, because the feed embeds every description and takes no parameter that
/// would make it smaller. Paging the html list costs many requests instead of one, but each of them is small and
/// bounded, which is the trade the biggest boards need.
///
/// Its data is worth less and that is the point of it being second: the tiles are configured per customer, so a title
/// is reliable but a location is only there when that site puts one on the tile, and there are no descriptions at all.
/// A board served this way is still complete - every vacancy is listed - which is what change detection needs.
/// </summary>
public sealed class CsbHtmlSearchStrategy(
    SuccessFactorsHtmlSearchClient client,
    IOptionsMonitor<SuccessFactorsOptions> options,
    ILog log) : ISuccessFactorsListStrategy
{
    private readonly ILog ctxLog = log.ForContext<CsbHtmlSearchStrategy>();

    public string Name => "html";

    public int Priority => 10;

    public bool CanServe(SuccessFactorsBoardConfig config) =>
        config.HasDomain && options.CurrentValue.EnableHtmlFallback;

    public async Task<SuccessFactorsFetch<SuccessFactorsListing>> FetchAsync(
        SuccessFactorsBoardConfig config,
        bool includeDescriptions,
        CancellationToken ct)
    {
        var opts = options.CurrentValue;

        var vacancies = new List<Vacancy>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // The site decides the step whatever is asked of it, so it is read off the first page instead of configured.
        var step = Math.Max(1, opts.HtmlPageSize);
        var startRow = 0;

        for (var page = 0; page < Math.Max(1, opts.MaxPages); page++)
        {
            ct.ThrowIfCancellationRequested();

            var response = await client.GetPageAsync(config, startRow, ct);

            if (!response.Success)
            {
                return new SuccessFactorsFetch<SuccessFactorsListing>(
                    null, response.NotFound && page == 0, response.Truncated, response.Error);
            }

            var tiles = response.Value!;

            if (tiles.Count == 0)
                return Done(config, vacancies, Name);

            if (page == 0)
                step = tiles.Count;

            var fresh = 0;

            foreach (var tile in tiles)
            {
                if (!seen.Add(tile.Id))
                    continue;

                fresh++;
                vacancies.Add(ToVacancy(tile, config));
            }

            // A site that runs out of rows answers with the last page again rather than an empty one, so a page that
            // brings nothing new is the end of the board however many rows were asked for.
            if (fresh == 0 || tiles.Count < step)
                return Done(config, vacancies, Name);

            startRow += step;
        }

        ctxLog.Warn(
            "Board {Board}: page cap {Cap} reached after {Count} vacancies — the html listing is incomplete",
            config.BoardId, opts.MaxPages, vacancies.Count);

        // An incomplete listing must not be committed: the vacancies past the cap would read as closed.
        return SuccessFactorsFetch<SuccessFactorsListing>.Failure(
            $"page cap reached: {vacancies.Count} vacancies");
    }

    private static SuccessFactorsFetch<SuccessFactorsListing> Done(
        SuccessFactorsBoardConfig config,
        IReadOnlyList<Vacancy> vacancies,
        string strategy) =>
        SuccessFactorsFetch<SuccessFactorsListing>.Ok(new SuccessFactorsListing
        {
            Vacancies = vacancies,
            Strategy = strategy,
            Locale = config.Locale
        });

    private static Vacancy ToVacancy(JobTileDto tile, SuccessFactorsBoardConfig config)
    {
        var url = tile.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? tile.Url
            : $"{config.SiteUrl}/{tile.Url.TrimStart('/')}";

        return new Vacancy
        {
            SourceId = SuccessFactorsMapper.SourceId,
            BoardId = config.BoardId,
            PostId = tile.Id,
            Title = string.IsNullOrWhiteSpace(tile.Title) ? tile.Id : tile.Title,
            Location = tile.Location,
            Url = url
        };
    }
}
