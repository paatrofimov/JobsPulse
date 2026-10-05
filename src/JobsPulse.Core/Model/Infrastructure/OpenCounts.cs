namespace JobsPulse.Core.Model.Infrastructure;

/// <summary>How many matching vacancies were open at one moment, and in how many companies.</summary>
public sealed record OpenCounts(int Vacancies, int Companies);
