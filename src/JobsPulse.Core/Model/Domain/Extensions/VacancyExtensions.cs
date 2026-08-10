using System.Text;
using JobsPulse.Core.Helpers;
using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Core.Model.Domain.Extensions;

public static class VacancyExtensions
{
    public static string ToStringForHash(this Vacancy vacancy)
    {
        var sb = new StringBuilder();

        sb.Append(vacancy.Title);
        sb.Append(vacancy.Location);
        sb.Append(vacancy.Url);
        sb.AppendList(nameof(vacancy.Offices), vacancy.Offices);

        return sb.ToString();
    }

    /// <summary>
    /// The watchlist is part of the key: one vacancy legitimately produces one notification per watchlist, and
    /// idempotency must still hold inside every one of them.
    /// </summary>
    public static string ToDedupKey(
        this Vacancy vacancy,
        VacancyChangeKind changeKind,
        string contentHash,
        long? watchlistId)
    {
        return $"{vacancy.Key}|{watchlistId?.ToString() ?? "-"}|{changeKind}|{contentHash}";
    }
}
