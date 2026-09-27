using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Core.Pipeline;

/// <param name="TimeoutSeconds">Hard limit for a single board traversal.</param>
/// <param name="DryRun">Nothing is enqueued to the outbox.</param>
/// <param name="StorageFilters">Union of the enabled watchlist filters - decides what is stored globally.</param>
/// <param name="StorageFilterHash">Hash of that set, stored per row so a filter change can be detected.</param>
/// <param name="DescriptionRulesHash">Hash of its description rules, stored per row so a known posting is re-read
/// once they change.</param>
public readonly record struct BoardProcessSettings(
    int TimeoutSeconds,
    bool DryRun,
    IReadOnlyList<FilterSpec> StorageFilters,
    string StorageFilterHash,
    string DescriptionRulesHash);
