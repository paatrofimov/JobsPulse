# Routines

## TelegramBotListener

Listening user commands and passing handling to command router.

On start publishes `BotCommandCatalog.All` via `setMyCommands`, so the client shows the command menu.
Replies longer than the Telegram message limit are split by lines before sending (`/show_state`).

# Infrastructure

## CommandRouter

Responsible for implementing user scenarios business-logic. Returns HTML-response in the same markup as
`MessageFormatter` (`<h6>`, `<p>`, `<br>`) - plain `\n` is collapsed by the renderer.

Uses pending selection store for storing short dialogue states.

Manages watchlist entries (resolving by name/url, adding/removing, enabling/disabling etc.) on user request.

- /watch CompanyName → search → board candidates list → «1» → added
- /watch &lt;url&gt; → resolve career page
- /list → list watched entries
- /remove CompanyName → unwatch company
- /force_cycle → `PollingOrchestrator.TryRunCycleAsync`; answers «already running» instead of starting a second cycle
- /show_state → every row of `seen_vacancy` is wrapped into `OutboxItem` (open → `New`, closed → `Closed`) and sent
  through `IVacancySink`, so the dump looks exactly like a real notification; the reply itself is only a summary
- /drop_data → wipes `seen_vacancy` and `outbox`
- /boards [source] → registry counts per source + top boards by vacancies count
- /board_remove &lt;source&gt; &lt;board&gt; → drops one row from the registry
- /discover → forced full re-walk of crawl indexes; started detached (it takes hours) and reports into the log
- /help

## BotCommandCatalog

Command names, descriptions and `/help` text in one place - used both for routing in `CommandRouter` and for the
Telegram command menu (`setMyCommands`). Legacy aliases (`/add`, `/unwatch`, `/start`) stay in the router only.

## PendingSelectionStore

Store dialogue states «/watch CompanyName → 1». Stored in memory because dialogue session lasts only a few seconds - no need to restart.

