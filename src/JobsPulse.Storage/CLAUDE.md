Persistence layer: PostgreSQL + Npgsql + EF Core.

Implements `IStateStore` and `IOutboxStorage` from `JobsPulse.Core.Abstractions`. Domain models never leave the
storage layer as persistent models - conversion happens in `PersistencyExtensions`.

Tables: `seen_vacancy` (current state of a board), `outbox` (notifications to deliver), `board_registry`
(accumulative list of boards that exist) and `crawl_index_state` (which crawl indexes were already mined).
No `watchlist` table yet - watchlist lives outside this project.

# Infrastructure

## StorageServiceCollectionExtensions

`AddStorage(config, connectionStringName)` registers:

- `NpgsqlDataSource` as a singleton - shared by EF and by raw ADO.NET commands, so both use one connection pool.
- `IDbContextFactory<JobsPulseDbContext>` instead of a scoped `DbContext` - consumers are singletons/background
  routines, and a `DbContext` is not thread-safe. Every method creates and disposes its own context.
- `UseSnakeCaseNamingConvention()` (EFCore.NamingConventions) - C# `PostId` maps to `post_id` automatically, so
  hand-written SQL in `StateStore` matches the EF model without explicit column mappings.
- `IStateStore` and `IOutboxStorage` as singletons; implementations are `internal`.

# Migrations

EF Core migrations, generated against `JobsPulseDbContext`. `20260808131550_InititalCreate` creates both tables and
all indexes. Column types come from the model: `text`, `text[]`, `jsonb`, `timestamp with time zone`, identity `bigint`.

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
- `content_hash`: change detection. Recomputed by `VacancyHasher` on write, not taken from the domain model.
- `first_seen_at` vs `first_published_at`: ours vs the board's. `first_published_at` is `COALESCE`d on update so the
  earliest known value is never overwritten by a later/absent one from the source.
- `updated_at`: write time of the source's `updated_at` - ATS boards bump their own timestamp on
  cosmetic edits, which is why hashing exists at all.
- `offices`: native `text[]`. Small, fixed, always read whole - a join table would buy nothing.

## PersistentOutboxItem

Table `outbox` - transactional outbox for notifications.

- `dedup_key`: unique, `ON CONFLICT DO NOTHING` on insert. Idempotency is enforced by the database, not by the
  pipeline, so a retried poll cannot enqueue the same change twice. Format is described in Core CLAUDE.md.
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

### LoadAllAsync / PurgeAllAsync

Admin-only paths behind bot commands. `LoadAllAsync` reads every row (closed included) ordered by source, board and
title. `PurgeAllAsync` deletes `outbox` first, then `seen_vacancy`, in one transaction - after it the next cycle
refills the boards from scratch.

## BoardRegistryStorage

Reads via EF, writes via raw Npgsql: the upsert needs `ON CONFLICT ... RETURNING (xmax = 0)` to tell a genuinely
new board from a refreshed one, which is what the discovery report counts. `MarkCrawlProcessedAsync` is the same
kind of idempotent upsert keyed by `(source_id, collection_id)`.

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
