# Routines

## TelegramBotListener

Listening user commands and passing handling to command router.

On start publishes `BotCommandCatalog.All` via `setMyCommands`, so the client shows the command menu.
Replies longer than the Telegram message limit are split by lines before sending (`/show_state`).

# Infrastructure

## MessageFormatter

One block per (watchlist, company, change kind) - the same vacancy may arrive for several watchlists at once, so the
watchlist is part of the block header. Blocks are ordered by watchlist, then by their freshest vacancy, and vacancies
inside a block by freshness too. Freshness is
`FirstPublishedAt`, falling back to `UpdatedAt`. Anything published within `Delivery:FreshVacancyDays` is
highlighted with 🔥 and bold text.

## CommandRouter

Responsible for implementing user scenarios business-logic. Returns HTML-response in the same markup as
`MessageFormatter` (`<h6>`, `<p>`, `<br>`) - plain `\n` is collapsed by the renderer.

Uses pending selection store for storing short dialogue states.

Manages watchlists and their entries through `WatchService`, i.e. straight into PostgreSQL - the bot never writes a
config file.

A watchlist is addressed by numeric id or by name; a name with spaces goes in quotes (`"Platform / SRE"`), which is
what keeps the multi-argument commands parsable.

- /watchlists → all watchlists with their board counts and how many vacancies currently match
- /watchlist &lt;ref&gt; → one watchlist: filter, boards and their entry ids
- /watchlist_add &lt;name&gt; → create; /watchlist_remove &lt;ref&gt; → delete with its entries
- /watchlist_enable, /watchlist_disable &lt;ref&gt; → pause or resume polling of a whole watchlist
- /filter &lt;ref&gt; → show the filter; /filter &lt;ref&gt; &lt;json&gt; → replace it (`FilterSpec` json, `{}` clears it)
- /board_add &lt;ref&gt; &lt;source&gt; &lt;board&gt; [company] → add a board; the ATS is probed for the name when it is omitted
- /board_remove &lt;ref&gt; &lt;entryId&gt; → drop one board from one watchlist
- /watch &lt;ref&gt; CompanyName|url → search → board candidates list → «1» → added to that watchlist
- /force_cycle → `PollingOrchestrator.TryRunCycleAsync`; answers «already running» instead of starting a second cycle
- /show_state → every row of `seen_vacancy` is wrapped into `OutboxItem` (open → `New`, closed → `Closed`) and sent
  through `IVacancySink`, so the dump looks exactly like a real notification; the reply itself is only a summary
- /drop_data → wipes `seen_vacancy`, `watchlist_vacancy` and `outbox`; the watchlists are configuration and are kept
- /boards [source] → registry counts per source + top boards ranked by matching (stored open) vacancies, board
  size only breaks ties
- /registry_remove &lt;source&gt; &lt;board&gt; → drops one row from the registry
- /discover → forced full re-walk of crawl indexes; started detached (it takes hours) and reports into the log
- /help

## BotCommandCatalog

Command names, descriptions and `/help` text in one place - used both for routing in `CommandRouter` and for the
Telegram command menu (`setMyCommands`). Legacy aliases (`/add`, `/unwatch`, `/start`) stay in the router only.

## PendingSelectionStore

Store dialogue states «/watch &lt;watchlist&gt; CompanyName → 1», including the target watchlist - the answer «1» carries
no destination of its own. Stored in memory because a dialogue session lasts only a few seconds.

