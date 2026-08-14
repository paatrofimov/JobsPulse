using System.Text.RegularExpressions;
using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Infrastructure;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Core.Pipeline;

public sealed class VacancyMatcher(TimeProvider clock, ILog log)
{
    private readonly ILog ctxLog = log.ForContext<VacancyMatcher>();

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);

    public bool Matches(Vacancy v, FilterSpec f)
    {
        if (f.IsEmpty) return true;

        if (f.TitleNoneOf.Count > 0 && AnyMatch(v.Title, f.TitleNoneOf, f.MatchMode))
            return false;

        if (f.LocationNoneOf.Count > 0 && AnyMatch(v.Location, f.LocationNoneOf, f.MatchMode))
            return false;

        if (f.DescriptionNoneOf.Count > 0 && AnyMatch(v.Description, f.DescriptionNoneOf, f.MatchMode))
            return false;

        if (f.TitleAnyOf.Count > 0 && !AnyMatch(v.Title, f.TitleAnyOf, f.MatchMode))
            return false;

        if (f.LocationAnyOf.Count > 0)
        {
            var hit = AnyMatch(v.Location, f.LocationAnyOf, f.MatchMode)
                      || v.Offices.Any(o => AnyMatch(o, f.LocationAnyOf, f.MatchMode));
            if (!hit)
                return false;
        }

        if (f.DescriptionAnyOf.Count > 0 &&
            !AnyMatch(v.Description, f.DescriptionAnyOf, f.MatchMode))
            return false;

        if (f.PostedWithinDays is { } days)
        {
            var since = v.FirstSeenAt ?? v.UpdatedAt;
            if (since < clock.GetUtcNow().AddDays(-days))
                return false;
        }

        return true;
    }

    public IReadOnlyList<Vacancy> Apply(IReadOnlyList<Vacancy> source, FilterSpec f)
    {
        if (f.IsEmpty)
            return source;
        return [.. source.Where(v => Matches(v, f))];
    }

    private bool AnyMatch(string? value, IReadOnlyList<string> patterns, FilterMatchMode mode)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        foreach (var p in patterns)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;

            var hit = mode switch
            {
                FilterMatchMode.Exact => string.Equals(value, p, StringComparison.OrdinalIgnoreCase),
                FilterMatchMode.Regex => SafeRegex(value, p),
                _ => value.Contains(p, StringComparison.OrdinalIgnoreCase)
            };

            if (hit)
                return true;
        }

        return false;
    }

    private bool SafeRegex(string value, string pattern)
    {
        try
        {
            return Regex.IsMatch(value, pattern,
                RegexOptions.IgnoreCase | RegexOptions.NonBacktracking, RegexTimeout);
        }
        catch (ArgumentException ex)
        {
            ctxLog.Warn(ex, "Invalid regex pattern: {Pattern}", pattern);
            return false;
        }
        catch (RegexMatchTimeoutException ex)
        {
            ctxLog.Warn(ex, "Regex match timed out for pattern: {Pattern}", pattern);
            return false;
        }
    }
}