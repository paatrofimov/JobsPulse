using System.ComponentModel.DataAnnotations;

namespace JobsPulse.Core.Options;

public sealed class DigestOptions
{
    public const string SectionName = "Digest";

    // The period the scheduled digest (`--role digest`) covers; its cadence is the scheduler's business
    [Range(1, 3650)] public int PeriodDays { get; set; } = 3;

    // The longest period a user may ask the bot for
    [Range(1, 3650)] public int MaxPeriodDays { get; set; } = 365;

    // A report after every polling, registry and discovery run
    public bool RunReports { get; set; } = true;
}
