namespace JobsPulse.Core.Model.Infrastructure;

/// <summary>
/// Language of everything the bot says to one user. Stored as int - reordering the enum must not shift stored rows.
/// </summary>
public enum BotLanguage
{
    English = 0,
    Russian = 1
}
