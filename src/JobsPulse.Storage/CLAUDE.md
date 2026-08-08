Persistence layer: PostgreSQL + Npgsql + EF Core.

Implements `IStateStore` and `IOutboxStorage` from `JobsPulse.Core.Abstractions`. Domain models never leave the
storage layer as persistent models - conversion happens in `PersistencyExtensions`.

Two tables only: `seen_vacancy` (current state of a board) and `outbox` (notifications to deliver).
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
- `updated_at`: write time of the row, not the source's `updated_at` - ATS boards bump their own timestamp on
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

## OutboxStorage

Pure EF. `ReadAndLease` selects due `Pending` items and flips them to `Leased` inside a transaction, so two
dispatchers cannot pick the same item. Terminal transitions use `ExecuteUpdate` - single round trip, and
`Attempts + 1` is computed by the database, so concurrent failures cannot lose an increment.

Known gap: a lease has no expiry or owner. If a dispatcher dies after leasing, the item stays `Leased` forever -
there is no reaper. `MarkAsDeadLetterAsync` only moves exhausted `Pending` items to `Dead`.

## Time

All timestamps are `timestamptz` / `DateTimeOffset`, always UTC. Both storages take `TimeProvider` instead of
`DateTimeOffset.UtcNow` so tests can control the clock.
