namespace JobsPulse.Core.Model.Infrastructure;

/// <summary>What `StackDetector` read out of a vacancy.</summary>
public readonly record struct StackReading(bool Primary, bool OtherRequired);
