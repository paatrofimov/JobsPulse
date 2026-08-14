using JobsPulse.Core.Model.Domain;
using JobsPulse.Sources.SuccessFactors.Models;

namespace JobsPulse.Sources.SuccessFactors.Infrastructure;

/// <summary>
/// Feed item to <see cref="Vacancy"/>.
///
/// Two mapping decisions are worth keeping:
///
/// - the feed has no publication date. '&lt;g:expiration_date&gt;' is not one, and the sites refresh their postings on
///   their own schedule, so deriving a date from it would rewrite the content hash for no reason. Both dates stay
///   null and change detection rests on the hash alone - the same call the Workday source makes about 'postedOn'.
///   It costs nothing: age filtering in `VacancyMatcher` uses `FirstSeenAt`, which is stamped here from the clock -
///   the field is not part of the content hash, so stamping it on every poll changes nothing.
/// - the location is not repeated into `Offices`. `VacancyMatcher` already matches `Location`, so a copy would only
///   pad the content hash without ever changing an answer.
/// </summary>
public sealed class SuccessFactorsMapper(TimeProvider clock)
{
    public const string SourceId = "successfactors";

    /// <summary>Null for an item that carries no id - it cannot be followed across polls.</summary>
    public Vacancy? ToVacancy(JobFeedItemDto item, SuccessFactorsBoardConfig config)
    {
        var postId = SuccessFactorsPostingIdentity.PostId(item.Id, item.Link);

        if (string.IsNullOrEmpty(postId))
            return null;

        var location = Clean(item.Location);
        var title = Title(item.Title, location, postId);

        return new Vacancy
        {
            SourceId = SourceId,
            BoardId = config.BoardId,
            PostId = postId,
            Title = title,
            Location = location,
            FirstSeenAt = clock.GetUtcNow(),
            Url = Clean(item.Link) ?? $"{config.SiteUrl}/job/{postId}/",
            Description = Clean(item.Description)
        };
    }

    /// <summary>
    /// The feed appends the location to the title in brackets - 'Senior UX Designer (Prague 5, CZ, 158 00)' - which
    /// would put the location into the title of every vacancy the bot shows. It is taken off only when the brackets
    /// hold exactly the item's own location: a title that ends in brackets of its own ('Engineer (m/w/d)') has to
    /// survive untouched, and so does a vacancy whose real title happens to end the same way as its location.
    /// </summary>
    private static string Title(string? rawTitle, string? location, string postId)
    {
        var title = Clean(rawTitle);

        if (title is null)
            return postId;

        if (string.IsNullOrEmpty(location))
            return title;

        var suffix = $" ({location})";

        return title.EndsWith(suffix, StringComparison.Ordinal) && title.Length > suffix.Length
            ? title[..^suffix.Length].TrimEnd()
            : title;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
