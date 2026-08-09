namespace JobsPulse.Core.Pipeline;

public readonly record struct FilterMaintenanceReport(int Checked, int Removed, int Retained)
{
    public static readonly FilterMaintenanceReport Empty = new(0, 0, 0);
}
