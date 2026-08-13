The bot is the whole user interface of the system. It is built for somebody who has just opened it and knows nothing:
menus and inline buttons, no slash commands to memorize, no internal ids on screen. Everything technical - raw board
ids, filter json, the registry, the pipeline - lives in the admin section behind `Telegram:AdminChatIds`.

# Users and ownership

Any telegram user may talk to the bot; `Telegram:AllowedUserIds` (empty by default) is there to lock that down.
`Telegram:AdminUsernames` (`patrofimov`) is *not* an access list - it only unlocks the admin section.
`Telegram:AdminChatIds` is the fallback for an administrator who has no username; both are checked by
`TelegramOptions.IsAdmin`. A username rather than a chat id, because that is the identity a person knows about
themselves.

Every person is a row in `bot_user` (`IBotUserStorage`), upserted on every incoming update, and a watchlist carries the
owner's telegram user id. That gives the three access levels the interface is built on:

- own watchlist - editable;
- somebody else's - visible as an example, no editing buttons at all (an action that would be refused is never
  offered);
- system watchlist (`owner_user_id IS NULL`) - editable by an admin only. Nothing produces one any more:
  `SystemWatchlistClaimer` hands the legacy ones to the administrator, and `/watchlist_add` records them as owned. The
  level is kept because a database can still hold such a row.

`WatchlistAccess` is the single chokepoint that decides this. Screens never compare owner ids themselves, so a new
screen cannot forget the check.

# Routines

## TelegramBotListener

Long-polls `getUpdates` for `Message` and `CallbackQuery` and hands each update to `BotUpdateHandler`. One failing
update is logged and skipped - the offset has already moved, so retrying it forever would wedge the loop.

On start it publishes the user command menu once per language (`setMyCommands` takes a language code), so a Russian
client shows Russian descriptions.

# Pipeline

## BotUpdateHandler

The single entry point. Resolves the user (and hence the language and admin flag) into a `BotContext` - which is also
where `SystemWatchlistClaimer` runs - then decides what the update is:

- a **callback** - render the screen and *edit the message in place*, so the bot stays one screen instead of a growing
  wall of replies. The callback query is answered first: the edit may take a moment and a stuck spinner looks broken.
  An un-editable message (too old) falls back to sending a new one.
- **plain text while a step is armed** - the answer to a question the bot just asked (`UserSessionStore`). A session
  holding only candidate buttons is left alone: it waits for a tap, not for text.
- a **user command** - `/start`, `/menu`, `/language`, `/help`.
- an **admin command** - handed to `CommandRouter`, but only from an admin chat; everybody else gets a localized
  refusal and the menu.

## ScreenRouter

One switch from `CallbackAction` to a screen, so the whole navigation graph is readable in one place, plus the routing
of an awaited text answer back to the step that asked for it. An unknown or stale action falls back to the menu rather
than throwing. `SetLanguage` is handled before the switch - it is the one action that changes the context it renders
in, so the confirmation is already in the new language.

## Screens

One class per screen, each returning a `ScreenView` (html + keyboard + optional toast) and never sending anything
itself.

- `MainMenuScreen` - the root and the answer to `/start`. Explains in two sentences what a watchlist is, because a
  first-time user has no idea, and offers every entry point as a button.
- `WatchlistsScreen` - «my watchlists» (editable) and «all watchlists» (examples, own ones marked ⭐ and listed first).
  Both name the owner explicitly.
- `WatchlistScreen` - one watchlist: owner, state, company and match counts, the filter in words. Rename, filter,
  companies, vacancies, pause and delete, with a confirmation step before the delete.
- `FilterScreen` - the filter one rule at a time: title keywords, excluded words, locations, freshness. Answers are
  comma separated, `-` clears a rule. No json ever reaches a user.
- `CompaniesScreen` - the companies of a watchlist, **grouped by the source they are watched through**. The list itself
  answers «which are watched, which are off, which are done»: a glyph per row (▶️ / ⏸ / ✅), a legend, the CV date and
  the discovery mark. The rows are text and not buttons, which is what raises the page from 8 companies to 30: a
  button per company capped the page at the keyboard size and filled the screen with labels that only repeated the
  list. One `🔧 Change a company` button asks for a name instead (`PendingInputKind.CompanyName` →
  `CompanyList.Find`): an exact name opens the per-company screen - mark worked through, disable, remove - several
  matches become buttons, a miss leaves the step armed, because a miss is usually a typo.
- `DisabledCompaniesScreen` - every disabled company of the user across all their watchlists, one tap to restore.
  Without it a switched-off company is effectively lost inside some watchlist page.
- `AddCompanyScreen` - a name or a careers-page link, resolved by `WatchService.LookupAsync`; the candidates become
  buttons. The ATS and the board id are never asked for.
- `VacanciesScreen` - vacancies opened *by watchlist name*: pick a list, then read what matched it **grouped by
  company**, the same shape the notifications have. The feed is loaded whole (capped at 500, freshest first) and
  `VacancyPageBuilder` packs it into as few screens as the telegram message limit allows, so a normal watchlist is one
  page. The browsable counterpart of the push notifications.
- `LanguageScreen` - Russian / English, stored on the user so it also applies to notifications hours later.
- `AdminScreen` - the door to the operator commands, and a refusal for everybody else. It opens with the traversal
  progress block (`ProgressReporter`) and a `🔄 Refresh` button, because that is the one thing an operator wants
  without typing anything; the commands stay a typed list.

Every screen ends its keyboard with a navigation row (`⬅ Back` / `🏠 Menu`) built by `KeyboardBuilder.Build`, so there
is always a way out. A refused edit still lands on a usable screen with a toast, never on a dead end.

# Infrastructure

## Localization

`TextKey` is an enum of every user-facing string; `EnglishTexts` / `RussianTexts` are the two tables and `BotTexts`
reads them. Deliberately hand-written rather than resx or `CultureInfo`: the solution builds with
`InvariantGlobalization=true`, under which culture-based resource *and date* lookups silently fall back to English.
Month names therefore come from the table too (`BotTexts.FormatDate`), which is what makes a Russian notification
actually read as Russian.

The language applies to menus, buttons, hints, statuses, errors, the command menu and the delivered notifications.

## KeyboardBuilder

Inline keyboard rows, paging and the closing navigation row. `PageSize` is 8 - more buttons than that on one screen is
unreadable. The page label is a button only because a row needs one, so it points at the page it already shows.

## CallbackAction / CallbackData

The callback protocol: `"wo:12:3"` - a two-letter action code, an id and a page. Codes rather than enum names because
telegram caps callback data at 64 bytes and it still has to carry both numbers. The page is echoed through every
action so «back» returns to the page the user came from. Anything unparsable becomes `None` - a stale button from an
old message must not throw.

## UserSessionStore

Dialogue state per telegram user, in memory: what free text is awaited, for which watchlist, plus the candidate list
of an «add company» search. A step lasts seconds and losing it on a restart costs one tap back to the menu.

## WatchlistAccess

The ownership chokepoint - see above. Also resolves a company entry together with the watchlist holding it, which is
what the disabled-companies screen and every per-company action need.

## SystemWatchlistClaimer

Gives every ownerless watchlist - the legacy import - to the administrator the first time they talk to the bot. A
migration cannot do it: the telegram user id of a person is only learned from an incoming update. The claim is one
indexed `UPDATE`, but it sits on the path of every message, so it runs once per process per user; a failure un-marks
the user and is logged instead of breaking the update.

## VacancyPageBuilder

The grouped vacancy feed of one watchlist: a block per company (glyph, name, count) with its vacancies newest first,
manual companies before discovered ones - the same ordering `MessageFormatter` uses, so a browsed list and a pushed one
read alike.

Pages are packed by size rather than by a fixed count: blocks are appended while the *visible* length stays under
`PageBudget`, so a screen carries every vacancy that still fits. Visible length is measured with the markup and the
link targets stripped, because that is what telegram counts against its 4096 limit, and an `href` is by far the longest
part of a rendered vacancy. A company longer than one screen is continued under a repeated header.

## ProgressReporter / ProgressFormatter

The admin answer to «how far has the walk got». `ProgressReporter` gathers the three sources of truth - the in-memory
`ITraversalProgressTracker`, `IBoardDiscoveryService.GetProgressAsync` and the registry row counts - and
`ProgressFormatter` renders them: per traversal, the state of the current cycle (`done of planned`, errors) and the
dataset coverage (`covered of total`, percent), per source and in total, plus the mined share of the crawl indexes. A
source that exists only in the registry is named as «not swept yet» rather than left out - a missing row reads as
nothing to do.

One reporter for two entry points (the admin screen and `/progress`), so they can never show different numbers.
`ProgressFormatter` itself is static and does no IO, which is what makes it testable; both are English only, like the
rest of the operator surface.

## CompanyList

Ordering, source grouping and name lookup of a company list, kept out of the screen so all three can be read and
tested on their own. Ordering is source, then active before disabled, then manual before discovered, then name -
grouping only slices that order, so a group longer than a page continues under a repeated header. `Find` lets an exact
name win over a containing one, otherwise a company whose name is a prefix of another («Nebius» in «Nebius AI») could
not be addressed by typing it in full.

## BotFormatter

Rendering shared by the screens: the company status glyph, the owner label and the filter in words. One place, so
«active / disabled / worked through» looks identical in every list.

## MessageFormatter

One block per (watchlist, company, change kind, origin) - the same vacancy may arrive for several watchlists at once,
so the watchlist is part of the block header. Blocks are ordered by watchlist, then **manual companies before
discovered ones**, then by their freshest vacancy; vacancies inside a block by freshness too.

A block whose company came from discovery (`OutboxItem.Discovered`) is marked with 🔎, and its first batch - the `New`
one the promotion enqueues - reads `🔎 New company found · Company · Watchlist`. That block is the announcement of the
company itself: it carries the whole matching vacancy list, and the next poll reports nothing more, because
`DiscoveredBoardPromoter` has already written the match rows.

Freshness is `FirstPublishedAt`, falling back to `UpdatedAt`. Anything published within `Delivery:FreshVacancyDays` is
highlighted with 🔥 and bold text. Headers and dates are localized.

## TelegramSink

Delivers each notification to the **owner of the watchlist that produced it**, in that owner's language: watchlists are
per user, so one destination chat would hand somebody another person's vacancies. A watchlist with no owner, and the
synthetic items of `/show_state`, go to `Telegram:DefaultChatId`.

Watchlists and users are read once per batch, not once per item. A failure for any chat fails the whole batch, because
the outbox has no per-item delivery state - `OutboxDispatcher` then reschedules it unchanged.

## TelegramClientFacade

The only place that talks to `ITelegramBotClient`: send (with a keyboard), edit in place, answer a callback query,
publish the command menu, poll for updates. «Message is not modified» from an edit is treated as success - tapping the
same button twice is not an error. A failure to answer a callback query is swallowed: it must never break the screen
that was just rendered.

## CommandRouter

The **administrator** surface, unchanged in substance and reachable only from `Telegram:AdminUsernames`: raw ids, filter
json, the board registry and the pipeline. `AdminCommandCatalog` lists it; it is kept out of the telegram command menu
on purpose. A watchlist created here is owned by the admin who created it, so it is editable from the interface too -
an ownerless one would be reachable through these commands only.

- /watchlists → every watchlist with its owner, board counts and matches
- /watchlist &lt;ref&gt; → one watchlist: filter, boards and their entry ids
- /watchlist_add, /watchlist_remove, /watchlist_enable, /watchlist_disable
- /filter &lt;ref&gt; [json] → show or replace the raw `FilterSpec`
- /board_add &lt;ref&gt; &lt;source&gt; &lt;board&gt; [company], /board_remove &lt;ref&gt; &lt;entryId&gt;
  (a discovered entry is disabled, not deleted - that row is what stops the sweep re-adding it)
- /watch &lt;ref&gt; &lt;company|url&gt; → resolve a board, answer with a number to pick
- /force_cycle, /show_state, /drop_data, /boards [source], /registry_remove, /discover
- /progress → traversal progress of boards, sources and crawl indexes (the same block the admin screen opens with)

## PendingSelectionStore

The «answer with a number» state of the admin `/watch` flow only. The user interface uses `UserSessionStore`.

## BotCommandCatalog / AdminCommandCatalog

The user menu (`/start`, `/menu`, `/language`, `/help`, published per language) and the operator list. Two catalogs
because they have two audiences and two visibilities.
