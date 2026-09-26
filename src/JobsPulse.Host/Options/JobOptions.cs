namespace JobsPulse.Host.Options;

public sealed class JobOptions
{
    public const string SectionName = "Job";

    public int MaxRunMinutes { get; set; }

    public int DrainTimeoutMinutes { get; set; } = 5;

    public int MaxDrainRetryAfterSeconds { get; set; } = 60;
}
