namespace JobsPulse.Core.Pipeline;

/// <summary>What dropping a board from a watchlist actually did.</summary>
public enum EntryRemoveResult
{
    NotFound,

    /// <summary>The row is gone - a manually added board can be added again at any time.</summary>
    Removed,

    /// <summary>
    /// A discovered board is kept as a disabled row instead of being deleted: that row is what stops the registry
    /// sweep from promoting the very same board again on its next pass.
    /// </summary>
    Disabled
}
