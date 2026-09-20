namespace JobsPulse.Sinks.Telegram.Models;

/// <summary>
/// How a vacancy or company list is sliced. Four questions, one list:
///
/// - <see cref="Company"/> is the default - it matches the notifications, so a browsed list and a pushed one read
///   alike (on the company screen the same slot means «by the source the board is watched through»);
/// - <see cref="Location"/> answers «what is there in Europe», which a per-company list cannot give without opening
///   every block;
/// - <see cref="Month"/> answers «what appeared or moved recently», newest month first;
/// - <see cref="Activity"/> keeps the per-company blocks but orders them by how much moves on that board per month -
///   see <see cref="JobsPulse.Core.Model.Infrastructure.BoardActivity"/>.
/// </summary>
public enum VacancyGrouping
{
    Company,
    Location,
    Month,
    Activity
}
