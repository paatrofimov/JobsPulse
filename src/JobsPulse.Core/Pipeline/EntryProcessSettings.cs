namespace JobsPulse.Core.Pipeline;

/// <param name="TimeoutSeconds">Hard limit for a single board traversal.</param>
/// <param name="DryRun">Nothing is enqueued to the outbox.</param>
public readonly record struct EntryProcessSettings(int TimeoutSeconds, bool DryRun);
