using System.Web;
using JobsPulse.Sources.HeadHunter.Models;

namespace JobsPulse.Sources.HeadHunter.Infrastructure;

/// <summary>
/// Reads a HeadHunter link. Every regional site and every city subdomain of one ('spb.hh.ru') addresses the same
/// catalog, so the host is remembered but never part of the identity - one employer is one board wherever it was linked
/// from. Returns null for anything that is not a HeadHunter url: every resolver is asked about every url.
/// </summary>
public static class HeadHunterUrl
{
    /// <summary>
    /// The regional sites of the platform. `rabota.by` is HeadHunter's Belarusian site under its own brand - a link to
    /// it is as much an employer link as an `hh.ru` one, and its ids come from the same catalog, which is why one
    /// employer is one board across all of them.
    ///
    /// Deliberately only the sites known to exist: every entry also becomes two crawl index patterns, so a guessed
    /// domain costs the discovery pass a predicate that can never match.
    /// </summary>
    public static readonly string[] Domains = ["hh.ru", "hh.kz", "hh.uz", "hh.kg", "rabota.by"];

    public static HeadHunterUrlParts? Parse(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return null;

        if (uri.Scheme is not ("http" or "https"))
            return null;

        var host = uri.Host.ToLowerInvariant();

        if (!Domains.Any(d => host == d || host.EndsWith('.' + d, StringComparison.Ordinal)))
            return null;

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < segments.Length - 1; i++)
        {
            var id = Id(segments[i + 1]);
            if (id is null)
                continue;

            switch (segments[i].ToLowerInvariant())
            {
                case "employer":
                case "employers":
                    return new HeadHunterUrlParts { EmployerId = id, Host = host };

                case "vacancy":
                case "vacancies":
                    return new HeadHunterUrlParts { VacancyId = id, Host = host };
            }
        }

        // A vacancy is also shared as a search-result link that carries its id in the query.
        var query = HttpUtility.ParseQueryString(uri.Query);

        if (Id(query["employer_id"]) is { } employerId)
            return new HeadHunterUrlParts { EmployerId = employerId, Host = host };

        if (Id(query["vacancyId"] ?? query["vacancy_id"]) is { } vacancyId)
            return new HeadHunterUrlParts { VacancyId = vacancyId, Host = host };

        return null;
    }

    /// <summary>
    /// Catalog ids are numeric, and a segment that is not one is a page rather than an entity ('/employer/rating').
    /// A trailing anything ('1740?from=search') is already off the segment by the time it gets here.
    /// </summary>
    private static string? Id(string? segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
            return null;

        var trimmed = segment.Trim();

        return trimmed.Length > 0 && trimmed.All(char.IsAsciiDigit) ? trimmed : null;
    }
}
