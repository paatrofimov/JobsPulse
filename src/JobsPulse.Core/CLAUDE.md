# Watchlists

The watchlist is the processing boundary. A watchlist is a named set of boards plus one filter, stored in PostgreSQL
(`watchlist`, `watchlist_entry`); there can be many of them and they are independent. The config file carries
infrastructure settings only - nothing about what is watched, and no runtime change ever touches a file.

One board may belong to several watchlists, so vacancy state is split in two levels:

- `seen_vacancy` - global state of an ATS vacancy (source/board/post), not bound to any watchlist. It holds every
  vacancy matching *any* enabled watchlist filter, which is what keeps it bounded while the registry sweep walks
  thousands of boards.
- `watchlist_vacancy` - the match layer: this post passed the filter of this watchlist, and this content was reported
  to it. This is what lets one vacancy match in one watchlist, miss in another and produce a separate notification
  per watchlist. Outbox rows and their dedup keys carry the watchlist id for the same reason.

A board is fetched once per cycle no matter how many watchlists share it - see `WatchlistPlan`.

# Pipeline

## PollingOrchestrator

One `RunCycleAsync` call is one polling cycle over every board of every enabled watchlist. Driven by a hosted routine
outside Core.

### Flow:

- read enabled watchlists with their entries and filters, collapse them into a `WatchlistPlan`
- for every due board (scheduling state is per board, not per entry) run `BoardProcessor`
    - if the board is missing, every watchlist entry pointing at it is disabled
- aggregate the reports

## WatchlistPlan

`Build(watchlists)` turns the enabled watchlists into work:

- one `BoardWorkItem` per distinct board, carrying a `WatchlistSubscription` (watchlist id and name, company name,
  filter and filter hash) for every watchlist that wants it; the interval of a shared board is the smallest override
  among its owners
- `StorageFilters` - the union of all enabled watchlist filters - plus `StorageFilterHash`. A vacancy matching none of
  them cannot produce a notification anywhere, so it is not stored at all. A watchlist with an empty filter matches
  everything, and therefore makes the registry sweep store everything.

The plan is shared by `PollingOrchestrator`, `RegistryPollingService` and `FilterMaintenanceService`, so all three
agree on what relevant means and every stored row carries the same filter-set hash.

## BoardProcessor

One board traversal: fetch, detect, commit state and notifications. Shared by `PollingOrchestrator` (watchlist feed)
and `RegistryPollingService` (registry feed) - they differ only in which boards they feed here, whether anything is
subscribed at all, and what they do with a dead board, which is why `BoardMissing` is returned instead of being
handled inside.

### Flow:

- traverse the board once
- load the global seen vacancies of the board and its match layer (the latter is skipped when nothing is subscribed)
- `ChangeDetector` produces both levels in one pass
- build outbox notifications - one per change, so one per watchlist
- update state as a single transaction:
    - upsert seen vacancies, close the ones that are gone
    - upsert and delete match rows
    - enqueue outbox

### Scheduling

Due-ness is decided by `lastRunByBoard`, an in-memory dictionary keyed by `{source}/{board}` - nothing is persisted,
so after a restart every board is due at once. A board shared by several watchlists is still one entry there, which is
what makes the single fetch possible. Interval is the smallest `IntervalMinutesOverride` among the owning watchlists,
or `PollingIntervalMinutes`. Stamps are written after the whole cycle using the timestamp captured at cycle start, so
the interval is measured from cycle start and a failed board is not retried earlier than a successful one.

### Concurrency and timeouts

`RunCycleAsync` is serialized by a `SemaphoreSlim(1, 1)` - cycles never overlap, a forced wake-up arriving
mid-cycle waits for the running one. `lastRunByBoard` is therefore touched by one thread at a time.
`TryRunCycleAsync` takes the same gate with a zero timeout and returns `CycleRunResult.Busy` instead of queueing -
it backs the `/force_cycle` bot command. A forced cycle ignores `lastRunByBoard` completely and processes every
board, as if the process had just started.

All due boards are started at once and throttled by a `SemaphoreSlim` of `MaxConcurrentEntries`.
Each board gets its own linked CTS with `SingleEntryProcessTimeoutSeconds`; a cancellation is treated as a timeout
only `when (!ct.IsCancellationRequested)` - otherwise it is a real shutdown and must propagate.

### Bail-outs (no commit at all)

- Source id is not in `ISourceCatalog` - config drift, board is skipped.
- `BoardMissing` (HTTP 404) - every watchlist entry pointing at the board is disabled, so a dead board stops being
  polled everywhere at once.
- `!IsComplete` - partial data is dropped entirely, because missing posts would be detected as closed.

### Reports

`BoardReport` / `CycleReport` are logging-only aggregates, nothing reads them for control flow.

## RegistryPollingService

Secondary cycle over `board_registry`. Never touches the watchlists: boards that are already watched are filtered
out, so the priority cycle stays the only writer for them. Registry boards belong to no watchlist, so the cycle has
no subscriptions and produces no notifications - it only keeps the global vacancy state warm (which is what makes the
`/boards` ranking meaningful) and deactivates boards that stopped answering. With no enabled watchlist nothing is
relevant, and the cycle is skipped entirely.

The registry is walked round-robin - `BoardsPerCycle` boards per cycle from an in-memory cursor (after a restart
the walk simply starts over). Concurrency, the per-board pause and the cycle interval are separate options, so the
background traffic does not starve the watchlist polling or the discovery crawler. Cycles never overlap
(`TryRunCycleAsync` with a zero-timeout gate); a board answering 404 is deactivated in the registry
(`is_active = false`) instead of being deleted.

## FilterMaintenanceService

Every `seen_vacancy` row stores `filter_hash` - the hash of the *set* of enabled watchlist filters it passed. When
any watchlist filter changes, the rows whose hash is no longer in use are re-evaluated: the ones matching no
watchlist are deleted and the count is logged, the rest just get the new hash. Newly matching vacancies are not
fetched here - the next cycle finds them.

Runs at the start of every `PollingWorker` iteration and short-circuits when nothing is stale. With no enabled
watchlist the stored state is left untouched instead of being wiped.

The match layer is deliberately not touched here: it is reconciled by the next poll of the board, which is also what
turns a narrowed filter into a `Closed` notification for that watchlist.

`DescriptionAnyOf` is dropped from the filter copies used for re-evaluation - descriptions are not persisted, so that
rule cannot be re-checked offline and would otherwise wipe everything.

## ChangeDetector

Pure function - no IO, no clock (it only borrows `VacancyMatcher`). Takes one board fetch, the global seen map, the
existing match rows and the subscriptions, and returns both levels at once: seen upserts and closures, match upserts
and removals, and the per-watchlist changes.

### Deduplication

One job can be posted many times (locations, languages). Posts are deduplicated by `{GroupId}|{Location}`,
case-insensitive. Posts without `GroupId` (prospect posts) always pass through - they have no group to collapse.

### Storage level

A fetched vacancy is stored when it matches at least one of `StorageFilters`. Anything else is not stored, which for
the closure rule below makes it look exactly like a vacancy that left the board - and that is intended: a vacancy
that stops matching every watchlist is reported as closed.

### New / Updated (per watchlist)

Lookup is in the match rows of that watchlist, by `PostId`. Missing means `New`; present with a different
`ContentHash` means `Updated`. The hash is recomputed here, so a source that bumps its own `UpdatedAt` on cosmetic
edits produces nothing. A board added to a second watchlist reports its vacancies as `New` for that watchlist only -
the first one is not disturbed.

### Closed

Computed only when `Traverse.IsComplete` (the orchestrator already bails out earlier - this is a second guard).
`Seen` holds only open vacancies of the board, so anything in `Seen` and not among the upserts is closed.

Closed is computed on both levels: globally (the post is no longer stored for the board) and per watchlist (the post
no longer passes that watchlist's filter, whatever the reason).

Two consequences worth remembering:

- a vacancy that stops matching a watchlist filter is reported as closed to that watchlist and stays alive for the
  others;
- the present-set is built from post-dedup upserts - a duplicate post that loses deduplication is closed too.

The closed `Vacancy` is rebuilt from the stored row, not from the source (the post is gone), and reuses the stored
`ContentHash` so the dedup key stays stable.

## VacancyHasher

SHA-256 truncated to 32 hex chars. `Compute` hashes the fields listed in `VacancyExtensions.ToStringForHash`

## VacancyMatcher

Applies filter to a list of vacancies.

## WatchService

Backs the bot commands: watchlist CRUD (create, delete, enable/disable, filter, interval), entry CRUD
(add/remove/enable/disable) and board lookup. Everything goes through `IWatchlistStorage`, so a change is in
PostgreSQL the moment the command returns. Resolution itself lives in the source projects (`IBoardResolver`); this
service only orchestrates and filters.

A watchlist is addressed by numeric id or by name (`ResolveAsync`). `AddBoardAsync` probes the ATS - to fill the
company name when it is not given, so a typo in a board id is rejected instead of being polled forever, and to pick up
the source-specific configuration, which is why the probe runs even when the name was explicit. A board whose probe
fails is still added when a name was given; it simply has no configuration, and a source that needs one falls back to
parsing the board id.

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

An entry is unique per `(watchlist, source, board)`; re-adding a board refreshes its company name and re-enables it
instead of creating a second row. The same board in another watchlist is a separate entry, on purpose.
After a successful add `IPollingTrigger.RequestImmediateRun` wakes the polling loop - a board with no run stamp is due
at once.

# Infrastructure

## LoggingHttpClient

Every outgoing http request of every project goes through this wrapper instead of a raw `HttpClient`: the request is
logged at Debug with its full absolute url (a relative one is resolved against the base address), and the answer with
its status code and how long it took. A failure is logged the same way, with the elapsed time and the innermost
exception message, and then rethrown - the wrapper decides nothing, it only makes the traffic readable.

`GetAsync` covers every ATS whose list endpoint is a GET; `PostAsync` exists for Workday, whose careers backend takes
its paging in a json body.

The log context is `http:{name}` of the named client (`greenhouse`, `lever`, `smartrecruiters`, `ashby`, `workday`,
`common-crawl-index`, `common-crawl-data`), so it is always clear which integration a line belongs to.

`IHttpClientFactory.CreateLoggingClient(name, log)` is how the wrapper is built - in the `Add*Source` extensions for
the ATS clients, and inline in the resolvers that fetch a career page.

## PollingTrigger

Latching wake-up signal between `WatchService` and the polling routine. `RequestImmediateRun` is a no-op when a
request is already pending, so repeated adds do not queue extra cycles; a request raised while the cycle is running
is not lost - the next `WaitAsync` returns immediately. `WaitAsync` returns on the wake-up or after the period,
whichever comes first.

# Abstractions

## IStateStore

Responsible for atomic updates of seen vacancies, of the watchlist match layer and for enqueueing outbox
notifications - all in one transaction, so a notification can never exist without the state that produced it.
`LoadAllAsync` and `PurgeAllAsync` are admin operations exposed through bot commands, not used by the pipeline;
`PurgeAllAsync` wipes derived state (vacancies, matches, outbox, registry) and keeps the watchlists, which are
configuration.

## IWatchlistStorage

The watchlist configuration: enabled watchlists with entries and filters for the pipeline, plus the CRUD the bot
needs. The only source of truth - there is no in-memory copy and no config-file fallback.

## IVacancySink

Sink implementations must implement formatting and sending.

## IBoardResolver

Searching board via human-readable name - bot command /watch {company_name}.
`ProbeAsync` is also the validation step of board discovery - a token exists only if the ATS answers for it.

## IBoardUrlParser

ATS-specific knowledge for crawl index mining: which url patterns to ask the index for and how to read a board id
out of a captured url. Implemented in the source projects, consumed by `JobsPulse.Discovery`.

## IBoardRegistryStorage

The accumulative registry of boards known to exist (`board_registry`) plus the processed crawl indexes
(`crawl_index_state`). Independent from the watchlists: the registry is what exists, a watchlist is what we watch.

## IBoardDiscoveryService

Fills the registry. Implemented in `JobsPulse.Discovery`; Core only holds the contract so the bot does not depend
on the discovery project.

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

## Board configuration

`BoardCandidate`, `WatchlistEntry`, `RegisteredBoard`, `BoardWorkItem` and `SourceTarget` all carry a nullable
`Configuration` - source-specific board parameters as json, stored in a `jsonb` column on `watchlist_entry` and
`board_registry`. It is null for every ATS whose `BoardId` is the whole address, and exists because Workday needs a
host, a tenant and a site; the resolver fills it, and the source reads it instead of parsing the board id.

The board id stays the single identity string every unique index is built on - for Workday it is the canonical
`{host}/{tenant}/{site}` rendering of the configuration, so `/boards`, the logs and outbox dedup keys stay readable
and a board can still be added by hand.

## Watchlist / WatchlistEntry

The configuration aggregate: a watchlist with its filter and its entries. An entry is one board inside one watchlist.

## WatchlistSubscription / BoardWorkItem

One board plus every watchlist interested in it - the unit of polling work, built by `WatchlistPlan`.

## WatchlistMatch / WatchlistMatchKey

A row of the match layer and its logical key `(watchlistId, source, board, post)`.

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

### WatchlistId / WatchlistName

Which watchlist the notification belongs to, denormalized so a delivered message stays readable after the watchlist is
renamed or deleted. Null only for synthetic items (the `/show_state` dump).

### DedupKey

- Idempotency key. Single change won't be enqueued twice.
- Format: {Vacancy.Key}|{WatchlistId}|{ChangeKind}|{ContentHash} - the watchlist is part of the key because the same
  vacancy legitimately produces one notification per watchlist.
