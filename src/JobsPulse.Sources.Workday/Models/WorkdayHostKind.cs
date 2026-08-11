namespace JobsPulse.Sources.Workday.Models;

/// <summary>Which of the two public host schemes a careers site is served under.</summary>
public enum WorkdayHostKind
{
    /// <summary>{sub}.{cluster}.myworkdayjobs.com/{site}</summary>
    MyWorkdayJobs,

    /// <summary>{cluster}.myworkdaysite.com/recruiting/{tenant}/{site}</summary>
    MyWorkdaySite
}
