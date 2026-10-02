namespace JobsPulse.Core.Model.Infrastructure;

/// <summary>A company with a number - the row of a «top companies» list.</summary>
public sealed record CompanyCount(string CompanyName, int Count);
