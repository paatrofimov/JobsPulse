# Pipeline

## PollingOrchestrator

One `RunCycleAsync` call is one polling cycle over the whole watchlist. Driven by a hosted routine outside Core.

### Flow:

- read watchlist entries
- identify source
- traverse board
    - if board is missing, disable entry in watchlist
- filter matching board vacations
- load seen vacancies from the same source and board
- detect vacancies changes from stored seen vacancies
- if board is traversed for the first time
    - then mark watchlist entries as seeded
    - else build outbox notifications
- update state as a single transaction:
    - upsert new seen vacancy
    - insert closed vacancies
    - enqueue outbox

### Scheduling

Due-ness is decided by `_lastRunByEntry`, an in-memory dictionary - nothing is persisted, so after a restart every
entry is due at once. Interval is `WatchEntry.IntervalMinutesOverride` or `PollingIntervalMinutes`.
Stamps are written after the whole cycle using the timestamp captured at cycle start, so the interval is measured
from cycle start and a failed entry is not retried earlier than a successful one.

### Concurrency and timeouts

All due entries are started at once and throttled by a `SemaphoreSlim` of `MaxConcurrentEntries`.
Each entry gets its own linked CTS with `SingleEntryProcessTimeoutSeconds`; a cancellation is treated as a timeout
only `when (!ct.IsCancellationRequested)` - otherwise it is a real shutdown and must propagate.

### Bail-outs (no commit at all)

- Source id is not in `ISourceCatalog` - config drift, entry is skipped.
- `BoardMissing` (HTTP 404) - the entry is disabled in the watchlist, so a dead board stops being polled.
- `!IsComplete` - partial data is dropped entirely, because missing posts would be detected as closed.

### Reports

`EntryReport` / `CycleReport` are logging-only aggregates, nothing reads them for control flow.

## ChangeDetector

Pure function - no IO, no clock. Takes the entry, the traversal result, the filtered vacancies and the seen map,
and returns changes, upserts and closed post ids.

### Deduplication

One job can be posted many times (locations, languages). Posts are deduplicated by `{GroupId}|{Location}`,
case-insensitive. Posts without `GroupId` (prospect posts) always pass through - they have no group to collapse.

### New / Updated

Lookup in `Seen` is by `PostId`. Missing means `New`; present with a different `ContentHash` means `Updated`.
The hash is recomputed here, so a source that bumps its own `UpdatedAt` on cosmetic edits produces nothing.

### Closed

Computed only when `Traverse.IsComplete` (the orchestrator already bails out earlier - this is a second guard).
`Seen` holds only open vacancies of the board, so anything in `Seen` and not among the upserts is closed.

Two consequences worth remembering:

- `Seen` is not filtered, but `Matched` is - a vacancy that stops matching the filter is reported as closed.
- The present-set is built from post-dedup upserts - a duplicate post that loses deduplication is closed too.

The closed `Vacancy` is rebuilt from the stored row, not from the source (the post is gone), and reuses the stored
`ContentHash` so the dedup key stays stable.

## VacancyHasher

SHA-256 truncated to 32 hex chars. `Compute` hashes the fields listed in `VacancyExtensions.ToStringForHash`
(title, location, url, offices) - the set that defines "the posting really changed".
`ComputeFilterHash` does the same for `FilterSpec` and is what drives re-seeding.

## VacancyMatcher

Applies filter to a list of vacancies.

## WatchService

Backs the bot commands - lookup, add, remove, list. Resolution itself lives in the source projects
(`IBoardResolver`); this service only orchestrates and filters.

### Flow:

- already in wathclist
- if passed url instead of name then try parse career page
- if resolved by name then show board candidates
- nothing found

### LookupAsync

- Exact-ish match against the watchlist first (`Watchlist.Find`: id or company name, case-insensitive).
- `http://` / `https://` prefix switches to `ResolveByUrlAsync`, everything else goes to `ResolveByNameAsync`.
- Every registered source is asked. A resolver throwing is logged and skipped, so one broken ATS cannot break the
  whole lookup; `OperationCanceledException` still propagates.
- Candidates already in the watchlist are dropped, the rest are ordered `DirectSlug` first, then by `JobCount`,
  and capped at 5 - the list is rendered as a choice in a chat message.

### AddAsync

`Id` is `{sourceId}:{boardId}` - deterministic, so the same board cannot be added twice under different names.
`SeededAt` is left null so the first cycle is silent.

# Abstractions

## IStateStore

Responsible for atomic updates of seen vacancies and enqueueing outbox notifications.

## IVacancySink

Sink implementations must implement formatting and sending.

## IBoardResolver

Searching board via human-readable name - bot command /watch {company_name}

# Model Infrastructure

## FilterSpec

Filter specification.

### PostedWithinDays

Truncate old vacancies (null - no truncation).

### MatchMode

Case-insensitive

- Substring: substring, default
- Exact
- Regex: NonBacktracking, timeout — a bad pattern should not hang the worker

## BoardCandidate

Showed to user on search by name.

# Model Domain

## Vacancy

Normalized vacancy - common for all ATS.

### SourceId

The id of a source: greenhouse, lever, etc.

### BoardId

The id of a board inside a source, for example, 'board_token' for greenhouse.

### PostId

The id of a post inside a board, for example 'id' for greenhouse. Unique within a board.

### Key

Format: {SourceId}/{BoardId}/{PostId}

### GroupId

The id of a job itself. Single job can be repeated across many posts (different locations, languages, etc.). null for prospect posts - posts listing that is not tied to a specific job. For example '
internal_job_id' for greenhouse.

### ContentHash

UpdatedAt can be changed on any cosmetic changes. So hash is calculated on each db upsert by important fields instead.

## OutboxItem

### Id

Incremental LONG id.

### DedupKey

- Idempotency key. Single change won't be enqueued twice.
- Format: {Vacancy.Key}|{ChangeKind}|{ContentHash}
