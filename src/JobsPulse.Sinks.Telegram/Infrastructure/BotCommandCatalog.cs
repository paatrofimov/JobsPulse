using Telegram.Bot.Types;

namespace JobsPulse.Sinks.Telegram.Infrastructure;

/// <summary>Single source of truth for the bot menu, routing and /help.</summary>
public static class BotCommandCatalog
{
    public const string Watchlists = "watchlists";
    public const string Watchlist = "watchlist";
    public const string WatchlistAdd = "watchlist_add";
    public const string WatchlistRemove = "watchlist_remove";
    public const string WatchlistEnable = "watchlist_enable";
    public const string WatchlistDisable = "watchlist_disable";
    public const string Filter = "filter";
    public const string BoardAdd = "board_add";
    public const string BoardRemove = "board_remove";
    public const string Watch = "watch";
    public const string ForceCycle = "force_cycle";
    public const string ShowState = "show_state";
    public const string DropData = "drop_data";
    public const string Boards = "boards";
    public const string RegistryRemove = "registry_remove";
    public const string Discover = "discover";
    public const string Help = "help";

    public static readonly IReadOnlyList<BotCommand> All =
    [
        new() { Command = Watchlists, Description = "list watchlists" },
        new() { Command = Watchlist, Description = "show one watchlist with its boards and filter: /watchlist <name|id>" },
        new() { Command = WatchlistAdd, Description = "create a watchlist: /watchlist_add <name>" },
        new() { Command = WatchlistRemove, Description = "delete a watchlist: /watchlist_remove <name|id>" },
        new() { Command = WatchlistEnable, Description = "resume polling of a watchlist: /watchlist_enable <name|id>" },
        new() { Command = WatchlistDisable, Description = "pause a watchlist: /watchlist_disable <name|id>" },
        new() { Command = Filter, Description = "show or replace the filter: /filter <name|id> [json]" },
        new() { Command = BoardAdd, Description = "add a board: /board_add <name|id> <source> <board> [company]" },
        new() { Command = BoardRemove, Description = "drop a board from a watchlist: /board_remove <name|id> <entryId>" },
        new() { Command = Watch, Description = "find a board by company name or url: /watch <name|id> <company|url>" },
        new() { Command = ForceCycle, Description = "run a polling cycle right now" },
        new() { Command = ShowState, Description = "dump all stored vacancies" },
        new() { Command = DropData, Description = "wipe stored vacancies, matches and outbox" },
        new() { Command = Boards, Description = "discovered boards registry: /boards [source]" },
        new() { Command = RegistryRemove, Description = "drop a board from the registry: /registry_remove <source> <board>" },
        new() { Command = Discover, Description = "re-walk crawl indexes and refill the registry" },
        new() { Command = Help, Description = "commands list" }
    ];

    public static string RenderHelp()
    {
        var lines = All.Select(c => $"/{c.Command} — {MessageFormatter.Escape(c.Description)}");

        return $"<h6>Commands</h6><p>{string.Join("<br>", lines)}</p>"
               + "<p>A watchlist is addressed by its name or its numeric id. "
               + "Names with spaces go in quotes: <code>/board_add \"Platform / SRE\" greenhouse nebius</code></p>"
               + "<p>Filter json accepts the FilterSpec fields, for example:<br>"
               + "<code>/filter 1 {\"titleAnyOf\":[\"backend\",\"sre\"],\"postedWithinDays\":60}</code></p>";
    }
}
