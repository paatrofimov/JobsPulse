namespace JobsPulse.Core.Pipeline;

public readonly record struct CycleRunResult(bool Started, CycleReport Report)
{
    public static readonly CycleRunResult Busy = new(false, CycleReport.Empty);

    public static CycleRunResult Completed(CycleReport report) => new(true, report);
}
