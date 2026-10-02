namespace JobsPulse.Core.Model.Infrastructure;

/// <summary>A company with its <see cref="BoardActivity"/> inside a period - the row of the «most active» list.</summary>
public sealed record CompanyActivity(string CompanyName, BoardActivity Activity);
