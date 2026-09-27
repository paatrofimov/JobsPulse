using System.Security.Cryptography;
using System.Text;
using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Domain.Extensions;
using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Core.Pipeline;

public static class VacancyHasher
{
    public static string Compute(Vacancy v)
    {
        return Hash(v.ToStringForHash());
    }

    public static string ComputeFilterHash(FilterSpec f)
    {
        return Hash(f.ToStringForHash());
    }

    /// <summary>
    /// Hash of a whole set of filters - the storage decision depends on all enabled watchlists at once, so a change
    /// in any of them must invalidate the stored rows. Order-insensitive.
    /// </summary>
    public static string ComputeFilterSetHash(IReadOnlyList<FilterSpec> filters)
    {
        var parts = filters
            .Select(f => f.ToStringForHash())
            .OrderBy(s => s, StringComparer.Ordinal);

        return Hash(string.Join("\n", parts));
    }

    /// <summary>
    /// Hash of the description rules alone, across a set of filters. Descriptions are not stored, so a known posting
    /// keeps the verdict its text got when it was last read - valid only while these rules stay the same.
    /// Order-insensitive; a set without description rules has a hash too, so adding the first rule changes it.
    /// </summary>
    public static string ComputeDescriptionRulesHash(IReadOnlyList<FilterSpec> filters)
    {
        var parts = filters
            .Where(f => f.UsesDescription)
            .Select(f =>
                $"{f.MatchMode:g}|{string.Join(",", f.DescriptionAnyOf)}|{string.Join(",", f.DescriptionNoneOf)}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal);

        return Hash(string.Join("\n", parts));
    }

    private static string Hash(string input) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)))[..32];
}
