using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using JobsPulse.Core.Helpers;
using JobsPulse.Core.Infrastructure;
using JobsPulse.Sources.Lever.Models;
using JobsPulse.Sources.Lever.Options;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Sources.Lever.Infrastructure;

/// <summary>
/// Thin client over the Lever postings API. Unlike Greenhouse it pages (`skip`/`limit`) and accepts server-side
/// filters, so a narrow watchlist does not have to download the whole board.
///
/// The global and the EU instance differ only by host, so there is one client for both: the instance of a site is
/// probed once (<see cref="LeverRegionMap"/>) and every later request goes straight to it.
/// </summary>
public sealed class LeverPostingsClient(
    LoggingHttpClient http,
    LeverRegionMap regions,
    IOptionsMonitor<LeverOptions> options,
    ILog log)
{
    public const string HttpClientName = "lever";

    private readonly ILog ctxLog = log.ForContext<LeverPostingsClient>();

    public async Task<LeverFetch<List<PostingDto>>> GetPostingsAsync(
        string site,
        int skip,
        int limit,
        bool applyFilters,
        CancellationToken ct)
    {
        var lookup = await LookupRegionAsync(site, ct);

        // A failing instance must not look like an empty board - that would close every stored vacancy of it.
        if (lookup.Error is { } error)
            return LeverFetch<List<PostingDto>>.Failure(error);

        if (lookup.Region is null)
        {
            ctxLog.Debug("Lever site '{Site}' has no postings on any known instance", site);
            return LeverFetch<List<PostingDto>>.Ok([]);
        }

        return await GetAsync(lookup.Region, site, skip, limit, applyFilters, ct);
    }

    /// <summary>The instance a site lives on, or null when no instance knows it. Cached across calls.</summary>
    public async Task<LeverRegion?> GetRegionAsync(string site, CancellationToken ct) =>
        (await LookupRegionAsync(site, ct)).Region;

    /// <summary>Fallback host for urls built before the instance of a site is known.</summary>
    public LeverRegion DefaultRegion => EnabledRegions().First();

    private async Task<RegionLookup> LookupRegionAsync(string site, CancellationToken ct)
    {
        if (regions.TryGet(site, out var known))
            return new RegionLookup(known, null);

        string? lastError = null;

        // One unfiltered posting is enough: an unknown site answers `200 []`, so emptiness is the «not here» signal.
        foreach (var region in EnabledRegions())
        {
            var response = await GetAsync(region, site, skip: 0, limit: 1, applyFilters: false, ct);

            if (response.Success && response.Value!.Count > 0)
            {
                regions.Set(site, region);
                return new RegionLookup(region, null);
            }

            // A real failure is remembered but does not stop the search - the site may live on the other instance.
            if (!response.Success && !response.NotFound)
                lastError = response.Error;
        }

        return new RegionLookup(null, lastError);
    }

    private IReadOnlyList<LeverRegion> EnabledRegions()
    {
        var configured = options.CurrentValue.Regions
            .Select(LeverRegion.Find)
            .Where(r => r is not null)
            .Select(r => r!)
            .ToList();

        // An empty or broken configuration must not silently disable the source.
        return configured.Count > 0 ? configured : LeverRegion.All;
    }

    private async Task<LeverFetch<List<PostingDto>>> GetAsync(
        LeverRegion region,
        string site,
        int skip,
        int limit,
        bool applyFilters,
        CancellationToken ct)
    {
        var opts = options.CurrentValue;

        var url = new StringBuilder(
            $"{region.PostingsApiUrl}{Uri.EscapeDataString(site)}?mode=json&skip={skip}&limit={limit}");

        if (applyFilters)
        {
            AppendFilter(url, "location", opts.Location);
            AppendFilter(url, "team", opts.Team);
            AppendFilter(url, "department", opts.Department);
            AppendFilter(url, "commitment", opts.Commitment);
            AppendFilter(url, "level", opts.Level);
        }

        return await GetAsync(url.ToString(), ct);
    }

    private async Task<LeverFetch<List<PostingDto>>> GetAsync(string url, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return LeverFetch<List<PostingDto>>.Missing();

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(30);
                ctxLog.Warn("Lever is throttling 429: {Url}, asked to wait for: {Delay}", url, retryAfter);
                return LeverFetch<List<PostingDto>>.Failure($"rate limited, retry after {retryAfter.TotalSeconds:F0}s");
            }

            if (!response.IsSuccessStatusCode)
                return LeverFetch<List<PostingDto>>.Failure($"HTTP {(int)response.StatusCode}");

            var payload = await response.Content.ReadFromJsonAsync<List<PostingDto>>(
                JsonSerializerOptionsFactory.Instance, ct);

            return payload is null
                ? LeverFetch<List<PostingDto>>.Failure("empty response")
                : LeverFetch<List<PostingDto>>.Ok(payload);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return LeverFetch<List<PostingDto>>.Failure(ex.Message);
        }
    }

    private static void AppendFilter(StringBuilder url, string name, IReadOnlyList<string> values)
    {
        // Repeated keys are OR-ed by the API.
        foreach (var value in values.Where(v => !string.IsNullOrWhiteSpace(v)))
            url.Append($"&{name}={Uri.EscapeDataString(value)}");
    }

    /// <param name="Region">The instance the site lives on, null when no instance knows it.</param>
    /// <param name="Error">Set when an instance could not be asked at all.</param>
    private readonly record struct RegionLookup(LeverRegion? Region, string? Error);
}
