using System.Text.RegularExpressions;

namespace JobsPulse.Sources.SuccessFactors.Infrastructure;

/// <summary>
/// Identity of one posting. The feed carries exactly one identifier - the recruiting marketing job id - and it is
/// also the last segment of the job url ('/job/Prague-5-Senior-UX-Designer-158-00/1419687233/'), which is what makes
/// it recoverable from a listing that has no feed behind it.
///
/// It is not the requisition id. The two are different numbers, the requisition id is nowhere on the public site, and
/// recovering it would cost one request per vacancy against the apply endpoint - so there is no group id for this
/// source. That is deliberate rather than missing: `ChangeDetector` collapses posts by '{GroupId}|{Location}' and
/// lets a post without a group through untouched, so no group id is the answer that cannot silently merge two
/// unrelated vacancies. Inventing one out of the title would do exactly that.
/// </summary>
public static partial class SuccessFactorsPostingIdentity
{
    /// <summary>The trailing numeric segment of a job url, with or without the closing slash.</summary>
    [GeneratedRegex(@"/(\d{4,})/?$", RegexOptions.CultureInvariant)]
    private static partial Regex JobIdInUrl();

    /// <summary>
    /// The feed's own id, falling back to the one in the url. Null when neither is there - such an item cannot be
    /// identified across polls and is dropped by the mapper rather than given a synthetic id that changes every time.
    /// </summary>
    public static string? PostId(string? feedId, string? link)
    {
        var id = feedId?.Trim();

        if (!string.IsNullOrEmpty(id))
            return id;

        return FromUrl(link);
    }

    public static string? FromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        // The query and the fragment are cut off first: the id is the last segment of the path, not of the string.
        var path = url.Split('?', '#')[0];
        var match = JobIdInUrl().Match(path);

        return match.Success ? match.Groups[1].Value : null;
    }
}
