namespace JobsPulse.Core.Model.Infrastructure;

/// <summary>Who put a board into a watchlist. Stored as int - reordering the enum must not shift stored rows.</summary>
public enum BoardOrigin
{
    /// <summary>Added by hand through the bot.</summary>
    Manual = 0,

    /// <summary>Promoted from the discovery registry because its vacancies matched the watchlist filter.</summary>
    Discovery = 1
}
