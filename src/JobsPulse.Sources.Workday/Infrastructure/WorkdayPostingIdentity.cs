using System.Text.RegularExpressions;

namespace JobsPulse.Sources.Workday.Infrastructure;

/// <summary>
/// The identity of a Workday posting, read out of its `externalPath`. The last path segment ends with the
/// requisition token ('...-Engineer_JR2015943'), and Workday appends '-1', '-2' when one requisition is posted more
/// than once - so the token identifies the posting while the requisition identifies the job behind it.
///
/// The title is never part of the identity: it is the rest of that same segment, and a retitled vacancy must read as
/// an update, not as one vacancy closing and another opening.
/// </summary>
public static partial class WorkdayPostingIdentity
{
    /// <summary>Stable within a board. Falls back to the whole path when the segment carries no token.</summary>
    public static string PostId(string externalPath)
    {
        var token = Token(externalPath);

        return token ?? externalPath.Trim('/');
    }

    /// <summary>
    /// The requisition behind the posting, with the '-1' repost suffix dropped, so several postings of one
    /// requisition are deduplicated by <c>ChangeDetector</c> instead of being reported separately per location.
    /// </summary>
    public static string? GroupId(string externalPath, string? jobReqId)
    {
        if (!string.IsNullOrWhiteSpace(jobReqId))
            return jobReqId.Trim();

        var token = Token(externalPath);
        if (token is null)
            return null;

        var match = RepostSuffixPattern().Match(token);

        return match.Success ? match.Groups["req"].Value : token;
    }

    private static string? Token(string externalPath)
    {
        var lastSegment = externalPath.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        if (string.IsNullOrWhiteSpace(lastSegment))
            return null;

        var separator = lastSegment.LastIndexOf('_');
        if (separator < 0 || separator == lastSegment.Length - 1)
            return null;

        return lastSegment[(separator + 1)..];
    }

    /// <summary>
    /// 'JR2022750-1' - a requisition reposted, the suffix is Workday's own disambiguator. Matched greedily and
    /// limited to two digits, because a requisition id may itself contain a dash and a long number: 'JR-119418' is
    /// one id, not requisition 'JR' reposted 119418 times.
    /// </summary>
    [GeneratedRegex(@"^(?<req>.+)-(?<repost>\d{1,2})$")]
    private static partial Regex RepostSuffixPattern();
}
