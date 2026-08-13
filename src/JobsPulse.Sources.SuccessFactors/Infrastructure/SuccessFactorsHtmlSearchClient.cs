using System.Net;
using JobsPulse.Core.Infrastructure;
using JobsPulse.Sources.SuccessFactors.Models;

namespace JobsPulse.Sources.SuccessFactors.Infrastructure;

/// <summary>
/// One page of the html job list - the fragment the search page of a career site loads by ajax, which is the same
/// listing a candidate sees and the only one that exists on every career site builder site regardless of how it is
/// configured. Paging is 'startrow', in steps the site decides.
/// </summary>
public sealed class SuccessFactorsHtmlSearchClient(LoggingHttpClient http)
{
    /// <summary>A page of tiles is tens of kilobytes; a megabyte is a page that is not a page.</summary>
    private const int MaxBytes = 4 * 1024 * 1024;

    public async Task<SuccessFactorsFetch<IReadOnlyList<JobTileDto>>> GetPageAsync(
        SuccessFactorsBoardConfig config,
        int startRow,
        CancellationToken ct)
    {
        var url = config.TileSearchUrl(startRow);

        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return SuccessFactorsFetch<IReadOnlyList<JobTileDto>>.Missing();

            if (!response.IsSuccessStatusCode)
                return SuccessFactorsFetch<IReadOnlyList<JobTileDto>>.Failure($"HTTP {(int)response.StatusCode}");

            await using var body = await response.Content.ReadAsStreamAsync(ct);
            await using var budgeted = new ByteBudgetStream(body, MaxBytes);
            using var reader = new StreamReader(budgeted);

            var html = await reader.ReadToEndAsync(ct);

            return SuccessFactorsFetch<IReadOnlyList<JobTileDto>>.Ok(SuccessFactorsTileParser.Parse(html));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return SuccessFactorsFetch<IReadOnlyList<JobTileDto>>.Failure(ex.Message);
        }
    }
}
