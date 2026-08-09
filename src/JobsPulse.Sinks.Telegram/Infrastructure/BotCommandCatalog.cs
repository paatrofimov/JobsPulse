using Telegram.Bot.Types;

namespace JobsPulse.Sinks.Telegram.Infrastructure;

/// <summary>Single source of truth for the bot menu, routing and /help.</summary>
public static class BotCommandCatalog
{
    public const string Watch = "watch";
    public const string List = "list";
    public const string Remove = "remove";
    public const string ForceCycle = "force_cycle";
    public const string ShowState = "show_state";
    public const string DropData = "drop_data";
    public const string Boards = "boards";
    public const string BoardRemove = "board_remove";
    public const string Discover = "discover";
    public const string Help = "help";

    public static readonly IReadOnlyList<BotCommand> All =
    [
        new() { Command = Watch, Description = "start watching a company name or a career page URL" },
        new() { Command = List, Description = "list watched companies" },
        new() { Command = Remove, Description = "stop watching a company" },
        new() { Command = ForceCycle, Description = "run a polling cycle right now" },
        new() { Command = ShowState, Description = "dump all stored vacancies" },
        new() { Command = DropData, Description = "wipe stored vacancies and outbox" },
        new() { Command = Boards, Description = "discovered boards registry: /boards [source]" },
        new() { Command = BoardRemove, Description = "drop a board from the registry: /board_remove source board" },
        new() { Command = Discover, Description = "re-walk crawl indexes and refill the registry" },
        new() { Command = Help, Description = "commands list" }
    ];

    public static string RenderHelp()
    {
        var lines = All.Select(c => $"/{c.Command} — {MessageFormatter.Escape(c.Description)}");
        return $"<h6>Commands</h6><p>{string.Join("<br>", lines)}</p>";
    }
}
