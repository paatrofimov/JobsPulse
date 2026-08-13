namespace JobsPulse.Core.Model.Infrastructure;

/// <summary>Which traversal a progress report belongs to - the two cycles walk two different datasets.</summary>
public enum TraversalKind
{
    /// <summary>The priority cycle: every board of every enabled watchlist.</summary>
    Watchlist,

    /// <summary>The registry sweep: the round-robin walk over `board_registry`.</summary>
    Registry
}
