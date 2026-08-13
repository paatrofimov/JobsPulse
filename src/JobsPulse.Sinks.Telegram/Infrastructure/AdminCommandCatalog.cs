using Telegram.Bot.Types;

namespace JobsPulse.Sinks.Telegram.Infrastructure;

/// <summary>
/// The operator surface: everything that touches raw ids, json, the board registry or the pipeline itself. Kept out
/// of the telegram command menu on purpose - it is reachable from the admin screen and by typing, and only from a
/// chat listed in <c>Telegram:AdminChatIds</c>. English only: an operator reads the logs in English anyway.
/// </summary>
public static class AdminCommandCatalog
{
    public const string Admin = "admin";
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
    public const string Progress = "progress";

    public static readonly IReadOnlyList<BotCommand> All =
    [
        new() { Command = Watchlists, Description = "list every watchlist with its owner" },
        new() { Command = Watchlist, Description = "one watchlist with boards and filter: /watchlist <name|id>" },
        new() { Command = WatchlistAdd, Description = "create a system watchlist: /watchlist_add <name>" },
        new() { Command = WatchlistRemove, Description = "delete a watchlist: /watchlist_remove <name|id>" },
        new() { Command = WatchlistEnable, Description = "resume polling: /watchlist_enable <name|id>" },
        new() { Command = WatchlistDisable, Description = "pause a watchlist: /watchlist_disable <name|id>" },
        new() { Command = Filter, Description = "show or replace the filter json: /filter <name|id> [json]" },
        new() { Command = BoardAdd, Description = "add a board by raw ids: /board_add <ref> <source> <board> [company]" },
        new() { Command = BoardRemove, Description = "drop a board: /board_remove <ref> <entryId>" },
        new() { Command = Watch, Description = "resolve a board by name or url: /watch <ref> <company|url>" },
        new() { Command = ForceCycle, Description = "run a polling cycle right now" },
        new() { Command = ShowState, Description = "dump all stored vacancies" },
        new() { Command = DropData, Description = "wipe stored vacancies, matches, outbox and registry" },
        new() { Command = Boards, Description = "discovered boards registry: /boards [source]" },
        new() { Command = RegistryRemove, Description = "drop a registry row: /registry_remove <source> <board>" },
        new() { Command = Discover, Description = "re-walk crawl indexes and refill the registry" },
        new() { Command = Progress, Description = "traversal progress of boards, sources and crawl indexes" }
    ];

    public static bool IsAdminCommand(string command) =>
        command is Admin
            or Watchlists or Watchlist or WatchlistAdd or WatchlistRemove or WatchlistEnable or WatchlistDisable
            or Filter or BoardAdd or BoardRemove or Watch or ForceCycle or ShowState or DropData or Boards
            or RegistryRemove or Discover or Progress
            or "list" or "add" or "unwatch";
}
