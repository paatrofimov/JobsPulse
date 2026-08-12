using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Sinks.Telegram.Infrastructure.Localization;
using Telegram.Bot.Types;

namespace JobsPulse.Sinks.Telegram.Infrastructure;

/// <summary>
/// The commands an ordinary user sees in the telegram menu. Deliberately tiny: everything else is a button, so there
/// is nothing to memorize. Published per language, because `setMyCommands` takes a language code.
/// </summary>
public static class BotCommandCatalog
{
    public const string Start = "start";
    public const string Menu = "menu";
    public const string Language = "language";
    public const string Help = "help";

    public static IReadOnlyList<BotCommand> For(BotLanguage language) => language switch
    {
        BotLanguage.Russian =>
        [
            new() { Command = Start, Description = "начать и открыть меню" },
            new() { Command = Menu, Description = "главное меню" },
            new() { Command = Language, Description = "выбрать язык" },
            new() { Command = Help, Description = "как это работает" }
        ],
        _ =>
        [
            new() { Command = Start, Description = "start and open the menu" },
            new() { Command = Menu, Description = "main menu" },
            new() { Command = Language, Description = "choose a language" },
            new() { Command = Help, Description = "how it works" }
        ]
    };

    /// <summary>Both languages, so the client menu is localized whichever locale the user runs.</summary>
    public static IEnumerable<(BotLanguage Language, string Code, IReadOnlyList<BotCommand> Commands)> All()
    {
        foreach (var language in (BotLanguage[])[BotLanguage.English, BotLanguage.Russian])
            yield return (language, BotTexts.LanguageCode(language), For(language));
    }
}
