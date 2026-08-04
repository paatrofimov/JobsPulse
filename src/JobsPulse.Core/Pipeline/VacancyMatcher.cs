using System.Text.RegularExpressions;
using JobsPulse.Core.Model;

namespace JobsPulse.Core.Pipeline;

/// <summary>
/// Применение <see cref="FilterSpec"/> к вакансии. Чистая функция, без IO — основной объект юнит-тестов.
/// </summary>
public sealed class VacancyMatcher(TimeProvider clock)
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);

    public bool Matches(Vacancy v, FilterSpec f)
    {
        if (f.IsEmpty) return true;

        if (f.TitleNoneOf.Count > 0 && AnyMatch(v.Title, f.TitleNoneOf, f.MatchMode)) return false;
        if (f.LocationNoneOf.Count > 0 && AnyMatch(v.Location, f.LocationNoneOf, f.MatchMode)) return false;

        if (f.TitleAnyOf.Count > 0 && !AnyMatch(v.Title, f.TitleAnyOf, f.MatchMode)) return false;

        if (f.LocationAnyOf.Count > 0)
        {
            // Локация может лежать и в Location, и в Offices — проверяем оба места.
            var hit = AnyMatch(v.Location, f.LocationAnyOf, f.MatchMode)
                      || v.Offices.Any(o => AnyMatch(o, f.LocationAnyOf, f.MatchMode));
            if (!hit) return false;
        }

        if (f.DepartmentAnyOf.Count > 0 &&
            !v.Departments.Any(d => AnyMatch(d, f.DepartmentAnyOf, f.MatchMode)))
            return false;

        if (f.PostedWithinDays is { } days)
        {
            var since = v.FirstPublished ?? v.UpdatedAt;
            if (since < clock.GetUtcNow().AddDays(-days)) return false;
        }

        return true;
    }

    public IReadOnlyList<Vacancy> Apply(IReadOnlyList<Vacancy> source, FilterSpec f)
    {
        if (f.IsEmpty) return source;
        return source.Where(v => Matches(v, f)).ToList();
    }

    private static bool AnyMatch(string? value, IReadOnlyList<string> patterns, MatchMode mode)
    {
        if (string.IsNullOrEmpty(value)) return false;

        foreach (var p in patterns)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;

            var hit = mode switch
            {
                MatchMode.Exact => string.Equals(value, p, StringComparison.OrdinalIgnoreCase),
                MatchMode.Regex => SafeRegex(value, p),
                _ => value.Contains(p, StringComparison.OrdinalIgnoreCase)
            };

            if (hit) return true;
        }

        return false;
    }

    private static bool SafeRegex(string value, string pattern)
    {
        try
        {
            // NonBacktracking + таймаут: пользовательская регулярка не должна вешать воркер.
            return Regex.IsMatch(value, pattern,
                RegexOptions.IgnoreCase | RegexOptions.NonBacktracking, RegexTimeout);
        }
        catch (ArgumentException)
        {
            // Невалидный шаблон (или неподдерживаемая в NonBacktracking конструкция) — считаем «не совпало».
            return false;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
