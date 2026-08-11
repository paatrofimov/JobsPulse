using System.Text.RegularExpressions;
using JobsPulse.Sources.Workday.Models;

namespace JobsPulse.Sources.Workday.Infrastructure;

/// <summary>
/// Reads a board out of any public Workday url. The board url, the locale-prefixed one and a deep link to a single
/// vacancy all describe the same board, so all of them must collapse onto one address.
/// </summary>
public static partial class WorkdayBoardUrl
{
    /// <summary>Path segments that belong to the url scheme itself and can never be a site id.</summary>
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "job", "jobs", "details", "apply", "login", "wday", "recruiting", "assets", "static",
        "favicon.ico", "robots.txt", "sitemap.xml"
    };

    /// <summary>Everything from one of these onwards addresses a vacancy, not the board.</summary>
    private static readonly HashSet<string> BoardPathTerminators = new(StringComparer.OrdinalIgnoreCase)
    {
        "job", "details", "apply", "login", "userHome"
    };

    public static bool IsWorkdayHost(string host) =>
        host.EndsWith(".myworkdayjobs.com", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".myworkdaysite.com", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The site and, for the tenant, a hint only: the subdomain of a `myworkdayjobs.com` host is usually the tenant
    /// but not reliably, while a `myworkdaysite.com` url carries the real tenant in its path. A hint is never used
    /// without being confirmed - see <see cref="WorkdayBoardResolver"/>.
    /// </summary>
    public static WorkdayUrlParts? Parse(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return null;

        var host = uri.Host.ToLowerInvariant();
        if (!IsWorkdayHost(host))
            return null;

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Uri.UnescapeDataString)
            .ToList();

        return host.EndsWith(".myworkdaysite.com", StringComparison.OrdinalIgnoreCase)
            ? ParseWorkdaySite(host, segments)
            : ParseWorkdayJobs(host, segments);
    }

    /// <summary>{cluster}.myworkdaysite.com/recruiting/{tenant}/{site}[/...] - the tenant is explicit here.</summary>
    private static WorkdayUrlParts? ParseWorkdaySite(string host, List<string> segments)
    {
        var index = segments.FindIndex(s => s.Equals("recruiting", StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return null;

        var rest = Skip(segments, index + 1);

        // A locale may sit either before or after 'recruiting' depending on how the link was built.
        rest = StripLocale(rest);

        if (rest.Count < 2)
            return null;

        var tenant = rest[0];
        var site = rest[1];

        if (!IsUsableSegment(tenant) || !IsUsableSegment(site))
            return null;

        return new WorkdayUrlParts
        {
            Host = host,
            Site = site,
            TenantHint = tenant,
            Kind = WorkdayHostKind.MyWorkdaySite,
            IsBoardRoot = TrimToBoard(rest, 2).Count == 0
        };
    }

    /// <summary>{sub}.{cluster}.myworkdayjobs.com/[{locale}/]{site}[/...] - the subdomain is a hint only.</summary>
    private static WorkdayUrlParts? ParseWorkdayJobs(string host, List<string> segments)
    {
        var rest = StripLocale(segments);
        if (rest.Count == 0)
            return null;

        var site = rest[0];
        if (!IsUsableSegment(site))
            return null;

        // 'foo.wd3.myworkdayjobs.com' - the first label is the tenant hint, the second is the cluster.
        var subdomain = host.Split('.')[0];

        return new WorkdayUrlParts
        {
            Host = host,
            Site = site,
            TenantHint = string.IsNullOrWhiteSpace(subdomain) ? null : subdomain,
            Kind = WorkdayHostKind.MyWorkdayJobs,
            IsBoardRoot = TrimToBoard(rest, 1).Count == 0
        };
    }

    private static List<string> Skip(List<string> segments, int count) =>
        count >= segments.Count ? [] : segments[count..];

    private static List<string> StripLocale(List<string> segments) =>
        segments.Count > 0 && LocalePattern().IsMatch(segments[0])
            ? Skip(segments, 1)
            : segments;

    /// <summary>What is left after the board itself - empty for a board url, non-empty for a deep link.</summary>
    private static List<string> TrimToBoard(List<string> segments, int consumed)
    {
        var tail = Skip(segments, consumed);
        var terminator = tail.FindIndex(s => BoardPathTerminators.Contains(s));

        return terminator < 0 ? tail : tail[..terminator];
    }

    private static bool IsUsableSegment(string segment) =>
        !string.IsNullOrWhiteSpace(segment)
        && !Reserved.Contains(segment)
        && segment.Length <= 128
        && segment.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');

    /// <summary>'en-US', 'fr', 'zh-CN' - the optional locale prefix of a careers site url.</summary>
    [GeneratedRegex("^[a-z]{2}(-[A-Za-z]{2,4})?$", RegexOptions.IgnoreCase)]
    private static partial Regex LocalePattern();
}
