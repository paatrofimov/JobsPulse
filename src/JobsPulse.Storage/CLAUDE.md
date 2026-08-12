Persistence layer: PostgreSQL + Npgsql + EF Core.

Implements `IStateStore` and `IOutboxStorage` from `JobsPulse.Core.Abstractions`. Domain models never leave the
storage layer as persistent models - conversion happens in `PersistencyExtensions`.

Tables: `seen_vacancy` (current state of a board), `watchlist_vacancy` (which watchlist a vacancy matches),
`outbox` (notifications to deliver), `watchlist` / `watchlist_entry` (the watchlist configuration), `bot_user` (the
people using the bot), `board_registry` (accumulative list of boards that exist) and `crawl_index_state` (which crawl
indexes were already mined).
The watchlist configuration lives here now - there is no JSON watchlist any more.

# Infrastructure

## StorageServiceCollectionExtensions

`AddStorage(config, connectionStringName)` registers:

- `NpgsqlDataSource` as a singleton - shared by EF and by raw ADO.NET commands, so both use one connection pool.
- `IDbContextFactory<JobsPulseDbContext>` instead of a scoped `DbContext` - consumers are singletons/background
  routines, and a `DbContext` is not thread-safe. Every method creates and disposes its own context.
- `UseSnakeCaseNamingConvention()` (EFCore.NamingConventions) - C# `PostId` maps to `post_id` automatically, so
  hand-written SQL in `StateStore` matches the EF model without explicit column mappings.
- `IStateStore`, `IOutboxStorage`, `IBoardRegistryStorage`, `IWatchlistStorage` and `IBotUserStorage` as singletons;
  implementations are `internal`.

## DesignTimeDbContextFactory

Only for `dotnet ef migrations` - the tool needs a context without the host and without a reachable database. The
runtime context always comes from `AddStorage`.

# Migrations

EF Core migrations, generated against `JobsPulseDbContext`. `20260808131550_InititalCreate` creates both tables and
all indexes; `20260810175506_AddWatchlists` adds `watchlist`, `watchlist_entry`, `watchlist_vacancy` and the
`watchlist_id` / `watchlist_name` columns of `outbox`; `20260810191842_AddBoardConfiguration` adds the nullable
`configuration` column to `watchlist_entry` and `board_registry`; `20260812091506_AddBoardOrigin` adds
`watchlist_entry.origin` and `outbox.discovered`; `AddBotUsersAndOwnership` adds the `bot_user` table,
`watchlist.owner_user_id` (indexed - «my watchlists» is the most frequent read of the bot) and
`watchlist_entry.worked_at`. Nothing backfills the owner: a migration cannot know who it is, so pre-existing
watchlists stay system ones. Column types come from the model: `text`, `text[]`, `jsonb`, `timestamp with time zone`, identity `bigint`.

# PersistentModels

Models that are stored in the database and must be used only in the storage layer.

## PersistentSeenVacancy

Table `seen_vacancy` - last known state of every post ever observed on a board.

### Keys and indexes

- `id`: surrogate identity `bigint`. Never used for lookups - exists only to keep a stable narrow PK.
- Unique `(source_id, board_id, post_id)`: the logical identity of a post, mirrors `Vacancy.Key`.
  This is the `ON CONFLICT` target of the upsert - the upsert depends on this index existing.
- Partial `(source_id, board_id) WHERE closed_at IS NULL`: the polling hot path reads only open vacancies of one
  board, so closed rows are kept out of the index and it stays small as history grows.

### Why columns look like this

- `closed_at`: soft close. Rows are never deleted, so a vacancy that reappears keeps its `id` and `first_seen_at`,
  and history stays queryable. Reopening is just `closed_at = NULL` in the upsert.
- `filter_hash`: which filter the row passed. `FilterMaintenanceService` re-evaluates and deletes rows whose hash
  fell out of use, so a narrowed filter cleans the table instead of leaving stale rows behind.
- `content_hash`: change detection. Recomputed by `VacancyHasher` on write, not taken from the domain model.
- `first_seen_at` vs `first_published_at`: ours vs the board's. `first_published_at` is `COALESCE`d on update so the
  earliest known value is never overwritten by a later/absent one from the source.
- `updated_at`: write time of the source's `updated_at` - ATS boards bump their own timestamp on
  cosmetic edits, which is why hashing exists at all.
- `offices`: native `text[]`. Small, fixed, always read whole - a join table would buy nothing.

## PersistentOutboxItem

Table `outbox` - transactional outbox for notifications.

- `dedup_key`: unique, `ON CONFLICT DO NOTHING` on insert. Idempotency is enforced by the database, not by the
  pipeline, so a retried poll cannot enqueue the same change twice. The watchlist id is part of the key, so one
  vacancy can notify once per watchlist. Format is described in Core CLAUDE.md.
- `watchlist_id` / `watchlist_name`: which watchlist produced the notification. Denormalized and without a FK on
  purpose - a delivered message must stay readable after its watchlist is renamed or deleted. Null only for the
  synthetic items of `/show_state`.
- `discovered`: the board was promoted from the registry rather than added by hand. Denormalized for the same reason -
  the entry it came from may already be gone when the message is rendered. Defaults to `false`.
- `vacancy_payload`: `jsonb` snapshot of the vacancy, serialized with `JsonSerializerOptionsFactory.Instance`.
  Deliberately not a FK to `seen_vacancy` - that row is mutable, and a notification must be delivered exactly as it
  looked when the change was detected.
- `change_kind` and `status`: stored as `int` (enum values are explicit, so reordering the enum breaks nothing).
- Index `(status, next_attempt_at)`: the dispatcher's only query - pending items whose next attempt is due.
- `attempts`, `next_attempt_at`, `last_error`: retry bookkeeping owned by the storage layer.

## PersistentBoardRegistryEntry

Table `board_registry` - every board discovery has ever confirmed to exist.

- Unique `(source_id, board_id)`: the `ON CONFLICT` target. Re-discovering a board refreshes name, url and job
  count but keeps `discovered_via` / `discovered_at` - the origin of a board is history, not state.
- `discovered_via`: `common-crawl:{collection}` or `bot` - which discovery source produced the row.
- `is_active`: reserved for boards that stop answering; nothing flips it yet.
- `configuration`: `jsonb`, source-specific board parameters for an ATS a single slug cannot address (Workday). The
  upsert `COALESCE`s it, so a discovery pass that could not read one does not erase the stored one.

## PersistentWatchlist / PersistentWatchlistEntry

Tables `watchlist` and `watchlist_entry` - the configuration. The filter is a single `jsonb` column: it is always read
and written whole and never queried by field, so a column per rule would buy nothing. `name` is unique because the bot
addresses a watchlist by name; entries are unique per `(watchlist_id, source_id, board_id)` and are deleted with their
watchlist (cascade). An entry also carries the nullable `configuration` `jsonb` - the source-specific board
parameters the resolver produced, for an ATS whose board id is not the whole address - plus `origin`
(`BoardOrigin`, `int`, `0` = manual): who added the board, and `worked_at`: when the user marked the company as worked
through, null while no CV has gone out.

`watchlist.owner_user_id` is the telegram user id of the owner, without a FK to `bot_user`: the owner of a watchlist
must stay recorded even if the user row is ever cleaned up, the same reasoning as the denormalized `outbox` columns.
Null means a system watchlist - visible to everybody, editable by an admin only. The bot claims those rows for the
administrator on first contact, so in a live database the column is filled everywhere.

## PersistentWatchlistVacancy

Table `watchlist_vacancy` - the match layer, one row per `(watchlist, vacancy)`, unique on
`(watchlist_id, source_id, board_id, post_id)` (the `ON CONFLICT` target) with a second index on
`(source_id, board_id)` for the per-board read of a polling cycle.

Derived state on purpose: a row is deleted the moment the vacancy stops matching that watchlist, and the history of
what was sent stays in `outbox`. `content_hash` is the content last reported to this watchlist - the basis of the
`Updated` change - which is why it cannot live in `seen_vacancy`, where one row is shared by all watchlists.

## PersistentBotUser

Table `bot_user` - one row per person talking to the bot, unique on `telegram_user_id` (the identity a watchlist owner
is stored as; a chat id is not stable enough for that). `chat_id`, `display_name` and `last_seen_at` are refreshed on
every incoming update; `language` is a setting and is only ever written by the user.

## PersistentCrawlIndexState

Table `crawl_index_state` - one row per `(source_id, collection_id)`. A crawl index is written here only after it
has been fully scanned, so an interrupted run re-reads it, and a finished one is never read again.

## PersistentOutboxStatus

- Intermediate
    - Pending: ready for delivery if next attempt is due; can switch to Lease status
    - Lease: delivery is in progress; can switch to Delivered status on success OR Pending status on error with rescheduling
- Terminal
    - Delivered: message was delivered successfully
    - Dead: delivery attempts are exhausted

# Storages

## JobsPulseDbContext

Model configuration only - table names, keys, indexes, `text[]` and `jsonb` column types. Everything else is left to
the snake_case convention.

## StateStore

Reads via EF (`AsNoTracking`), writes via raw Npgsql commands.

Writes bypass EF on purpose: the commit needs `INSERT ... ON CONFLICT DO UPDATE ... WHERE content_hash IS DISTINCT
FROM` and `post_id = ANY(@post_ids)`, which EF cannot express, and it needs the real affected-row count.

### CommitAsync

Upserts, closures and outbox inserts run in one explicit transaction on one connection - a notification must never
be enqueued without the state change that produced it, and vice versa.

- Upsert: per-vacancy command in a loop, parameters re-bound each iteration. The `WHERE content_hash IS DISTINCT
  FROM` guard turns unchanged vacancies into zero-row no-ops, so the returned count is the number of real changes
  and unchanged rows are not touched.
- Close: single statement over `ANY(@post_ids)`, guarded by `closed_at IS NULL` so re-closing is a no-op.
- Enqueue: per-item insert with `ON CONFLICT (dedup_key) DO NOTHING`.

An empty commit short-circuits before opening a connection.

### Match layer

`UpsertMatchesAsync` and `RemoveMatchesAsync` run inside the same transaction as the vacancies and the outbox, so a
notification, the state that produced it and the match row that proves it was reported land together. The upsert is
guarded by `content_hash IS DISTINCT FROM` (plus the filter hash), so an unchanged match is a no-op; removals are one
statement per watchlist, because the composite key cannot be passed as a single array.

### LoadAllAsync / PurgeAllAsync

Admin-only paths behind bot commands. `LoadAllAsync` reads every row (closed included) ordered by source, board and
title. `PurgeAllAsync` deletes `outbox`, `watchlist_vacancy`, `seen_vacancy`, `board_registry` and `crawl_index_state` in one
transaction - after it the next cycle refills the boards from scratch. The watchlists themselves are configuration and
are never purged.

## BoardRegistryStorage

Reads via EF, writes via raw Npgsql: the upsert needs `ON CONFLICT ... RETURNING (xmax = 0)` to tell a genuinely
new board from a refreshed one, which is what the discovery report counts. `MarkCrawlProcessedAsync` is the same
kind of idempotent upsert keyed by `(source_id, collection_id)`.

## WatchlistStorage

Pure EF: the configuration is small, read whole (`Include(Entries)`) and written one row at a time. Every mutation is
committed immediately - the bot has no other way to change the configuration, and nothing caches it, so a change is
visible to the next polling cycle. `CreateAsync` rejects a duplicate name case-insensitively; `AddEntryAsync` refreshes
and re-enables an existing entry instead of inserting a second one (and refreshes its configuration, unless the
caller passed none - a probe that came back empty must not erase a stored address, and an explicit add also flips
`origin` back to manual); `DisableBoardAsync` switches off every entry pointing at a dead board, in every watchlist at
once; `ClaimOwnerlessAsync` is a single `ExecuteUpdate` over `owner_user_id IS NULL` - what a migration could not do,
because it does not know the telegram user id of the owner.

`AddDiscoveredEntryAsync` is the promotion path and is deliberately insert-only: any existing row for
`(watchlist, source, board)` - enabled or disabled - makes it return null, so a board the user has dropped is never
resurrected by the next registry sweep. A `DbUpdateException` from the unique index is treated the same way: a manual
add that landed between the check and the insert wins.

Entries come back ordered `origin` then company name, so every listing gets manual boards before discovered ones
without sorting again.

## BotUserStorage

Pure EF, and deliberately the cheapest thing here: every incoming telegram update touches it. One lookup by the unique
`telegram_user_id`, then a write. A concurrent insert of the same brand new user is caught as a `DbUpdateException` and
resolved by re-reading - the unique index decides, not the method.

## OutboxStorage

Pure EF. `ReadAndLease` selects due `Pending` items and flips them to `Leased` inside a transaction, so two
dispatchers cannot pick the same item. Terminal transitions use `ExecuteUpdate` - single round trip, and
`Attempts + 1` is computed by the database, so concurrent failures cannot lose an increment.

`PurgeDeliveredAsync` drops `Delivered` rows sent before a threshold - single `ExecuteDelete`, retention is decided
by the caller. Dedup keys of deleted rows can never come back: they carry the content hash of a change that is
already applied to `seen_vacancy`.

Known gap: a lease has no expiry or owner. If a dispatcher dies after leasing, the item stays `Leased` forever -
there is no reaper. `MarkAsDeadLetterAsync` only moves exhausted `Pending` items to `Dead`.

## Time

All timestamps are `timestamptz` / `DateTimeOffset`, always UTC. Both storages take `TimeProvider` instead of
`DateTimeOffset.UtcNow` so tests can control the clock.
