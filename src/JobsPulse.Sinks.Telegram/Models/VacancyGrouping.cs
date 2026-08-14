namespace JobsPulse.Sinks.Telegram.Models;

/// <summary>
/// How a vacancy or company list is sliced. <see cref="Company"/> is the default - it matches the notifications, so a
/// browsed list and a pushed one read alike; <see cref="Location"/> is the answer to «what is there in Europe», which
/// a per-company list cannot give without reading every block.
/// </summary>
public enum VacancyGrouping
{
    Company,
    Location
}
