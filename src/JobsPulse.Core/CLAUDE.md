# Watchlists

The watchlist is the processing boundary. A watchlist is a named set of boards plus one filter, stored in PostgreSQL
(`watchlist`, `watchlist_entry`); there can be many of them and they are independent. Each one belongs to the bot user
who created it (`OwnerUserId`, null for the legacy import - a system watchlist), which is what makes «my lists» and
«somebody else's lists as examples» possible, and what decides where its notifications are delivered. The config file carries
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
- `DescriptionRulesHash` - `VacancyHasher.ComputeDescriptionRulesHash` over `StorageFilters`: the description rules
  alone, so a title edit does not force every stored description to be read again.

The plan is shared by `PollingOrchestrator`, `RegistryPollingService` and `FilterMaintenanceService`, so all three
agree on what relevant means and every stored row carries the same filter-set hash.

## BoardProcessor

One board traversal: fetch, detect, commit state and notifications. Shared by `PollingOrchestrator` (watchlist feed)
and `RegistryPollingService` (registry feed) - they differ only in which boards they feed here, whether anything is
subscribed at all, and what they do with a dead board, which is why `BoardMissing` is returned instead of being
handled inside. `BoardProcessResult.Relevant` carries the vacancies that passed the storage filters, which is how the
registry cycle tests a board against the individual watchlist filters without fetching it twice.

### Flow:

- load the global seen vacancies of the board - handed to the source as `SourceTarget.Known`, together with
  `NeedsDescription` (a storage filter reads descriptions) and `MayBeStored` (`VacancyMatcher.MayMatchListed` against
  the storage filters), so the source can skip detail requests - see `DetailSelector`; for a source that
  `SelectsDetails` also its rejected postings (`IRejectedPostingStorage`) of the current filter set, as
  `SourceTarget.Rejected`
- traverse the board once
- load its match layer (skipped when nothing is subscribed)
- `ChangeDetector` produces both levels in one pass
- build outbox notifications - one per change, so one per watchlist
- stamp every upsert with the description rules hash of the plan (`Vacancy.DescriptionRulesHash`); a vacancy whose
  description was not read (`DescriptionUnavailable`) keeps the stamp it had, so the next poll reads it again
- update state as a single transaction:
    - upsert seen vacancies, close the ones that are gone, delete the ones still listed that pass no storage filter
    - upsert and delete match rows
    - enqueue outbox
- seen vacancies and match rows whose hashes equal the stored ones are dropped from the commit first - they mirror
  the storage guards, so the database result is the same, but an unchanged board commits nothing and costs no round
  trip (a changed description rules hash counts as a change). `BoardProcessResult.Relevant` still carries every
  vacancy that passed the storage filters.
- remember the rejected postings: every vacancy carrying a `ListHash` (its detail was read, or it was skipped as known
  rejected) that passes no storage filter. Only the difference to the stored set is written, outside the commit -
  losing it costs a detail request, not a notification.

### Scheduling

Due-ness is decided by `board_poll_state` (`IBoardPollStateStorage`), a row per `{source}/{board}` holding when that
board was last polled. The map is **read from the database at the start of every cycle**, not kept in a field: that
is what makes a restart continue the schedule instead of finding every board due at once and re-reading the whole
watchlist. A board shared by several watchlists is still one row there, which is what makes the single fetch
possible. Interval is the smallest `IntervalMinutesOverride` among the owning watchlists, or
`PollingIntervalMinutes`.

Stamps are written after the whole cycle, in one statement, using the timestamp captured at cycle start - so the
interval is measured from cycle start and a failed board is not retried earlier than a successful one. Failures are
stamped too: an unstamped board would stay the most-overdue one forever and the walk would never get past it.

### Concurrency and timeouts

`RunCycleAsync` is serialized by a `SemaphoreSlim(1, 1)` - cycles never overlap, a forced wake-up arriving
mid-cycle waits for the running one. The poll-state map is therefore loaded and stamped by one thread at a time.
`TryRunCycleAsync` takes the same gate with a zero timeout and returns `CycleRunResult.Busy` instead of queueing -
it backs the `/force_cycle` bot command. A forced cycle ignores the poll state completely and processes every
board, as if the process had just started.

All due boards are started at once and throttled by a `SemaphoreSlim` of `MaxConcurrentEntries`.
Each board gets its own linked CTS with `SingleEntryProcessTimeoutSeconds` (180: a board of ~5000 postings with a
description filter - Bosch on SmartRecruiters - needs about a minute); a cancellation is treated as a timeout
only `when (!ct.IsCancellationRequested)` - otherwise it is a real shutdown and must propagate.

### Bail-outs (no commit at all)

- Source id is not in `ISourceCatalog` - config drift, board is skipped.
- `BoardMissing` (HTTP 404) - every watchlist entry pointing at the board is disabled, so a dead board stops being
  polled everywhere at once.
- `!IsComplete` - partial data is dropped entirely, because missing posts would be detected as closed.

### Reports

`BoardReport` / `CycleReport` are logging-only aggregates, nothing reads them for control flow. `Stored` counts the
vacancies that passed the storage filters, `Matched` the match rows - always zero for a registry board.
`CycleReport.ChangesSince` is the earliest previous traversal among the boards a cycle walked (`EarliestPoll`, read
before the new stamps are written; boards walked for the first time are skipped) - the changes the cycle found piled
up since then. For the registry it is usually days back, since a board comes round once per walk. `Combine` keeps
the earliest of its slices.

## RegistryPollingService

Secondary cycle over `board_registry`. Boards that are already watched are filtered out, so the priority cycle stays
the only writer for them. A registry board has no subscriptions, so the sweep itself produces no notifications - it
keeps the global vacancy state warm (which is what makes the `/boards` ranking meaningful) and deactivates boards
that stopped answering. With no enabled watchlist nothing is relevant, and the cycle is skipped entirely.

What the sweep does produce is **promotions**: after the fetch pass every board is handed to
`DiscoveredBoardPromoter` together with its relevant vacancies. Promotion runs after the concurrent fetch pass, one
board at a time - it is database work only, so a single writer costs nothing and makes
`MaxAutoAddedBoardsPerCycle` exact. It also runs before `CycleFinished`: promotions enqueue notifications, and an
idle traversal lets the open delivery window leave. Reaching the cap is logged with how many boards of the slice were left for the
next cycle, because a silent cap reads as «nothing matched».

Candidates are enabled watchlists with a **non-empty** filter (`DiscoveredBoardPromoter.SelectCandidates`). A
watchlist matching everything would absorb the whole registry, so it is never filled automatically and the skip is
logged. `AutoAdd = false` and `DryRun` switch promotion off entirely.

The registry is walked **least-recently-polled first** - `BoardsPerCycle` boards per cycle, ordered by their
`board_poll_state` stamp (a board that has never been polled sorts first, so a fresh discovery is picked up before
anything else). There is no cursor any more: the order is derived from the stamps, so a restart continues the walk
where it stopped instead of starting the longest procedure of the installation over again. The whole slice is
stamped after the fetch pass, failures included, or the same board would be picked every cycle.

`TryRunSweepAsync(until)` is the one-shot job: cycle after cycle, leaving out boards already stamped in this call,
until the registry is done or the longest cycle so far (x1.5) no longer fits before `until` - so the deadline rarely
cuts a slice before it is stamped. One slice per run spent seconds of a half-hour budget and made a full walk a week
long. The long-living `all` role keeps one cycle per `CycleIntervalMinutes`.

The enabled watchlists, the registry (up to `MaxRegistryBoards` rows) and `board_poll_state` are read once per sweep
(`RegistrySweepState`), not once per slice: reloading them before every 50-board slice pulled gigabytes a day out of
Neon and exhausted its free egress quota. Slices stamp the in-memory poll map as well as the table. A watchlist edited
during a sweep takes effect with the next run.

Concurrency, the per-board pause and the cycle interval are separate options, so the
background traffic does not starve the watchlist polling or the discovery crawler. Cycles never overlap
(`TryRunCycleAsync` with a zero-timeout gate); a board answering 404 is deactivated in the registry
(`is_active = false`) instead of being deleted.

## DiscoveredBoardPromoter

Turns a board of the discovery registry into a watchlist entry. Per candidate watchlist: apply its filter to the
board's relevant vacancies, and if anything matches, `AddDiscoveredEntryAsync` the board and report it.

The match rows and the `New` notifications are written here, in the same commit as the promotion, on purpose: leaving
them to the next watchlist cycle would produce an ordinary `New` wave and the reader would never learn that a new
board appeared. The notifications carry `Discovered = true`, which is what turns them into one
`🔎 New board · Company · Watchlist` block. Because the match rows exist by then, the next poll of that board reports
nothing more.

`AddDiscoveredEntryAsync` is insert-only, so a board the user has dropped is never promoted again - see
`IWatchlistStorage` and `EntryRemoveResult`. The vacancies themselves are not upserted: the sweep has just committed
them. After a promotion `IPollingTrigger.RequestImmediateRun` wakes the watchlist loop - a fresh entry has no run
stamp and is due at once.

## FilterMaintenanceService

Every `seen_vacancy` row stores `filter_hash` - the hash of the *set* of enabled watchlist filters it passed. When
any watchlist filter changes, the rows whose hash is no longer in use are re-evaluated: the ones matching no
watchlist are deleted and the count is logged, the rest just get the new hash. Rows are read in batches of 5000,
batch after batch until nothing is stale, so one run applies the change to the whole table - the registry boards are
polled too rarely to clean up a leftover in the meantime. A batch that changed nothing ends the run instead of being
read again. Newly matching vacancies are not fetched here - the next cycle finds them.

Nothing is sent to the user from here; the notification comes from the poll of the board - see `ChangeDetector`.

Runs at the start of every `PollingWorker` iteration and short-circuits when nothing is stale. With no enabled
watchlist the stored state is left untouched instead of being wiped.

The match layer is deliberately not touched here: it is reconciled by the next poll of the board, which is also what
turns a narrowed filter into a `Filtered` notification for that watchlist.

`DescriptionAnyOf` and `DescriptionNoneOf` are dropped from the filter copies used for re-evaluation - descriptions are
not persisted, so those rules cannot be re-checked offline: the first would wipe everything, the second would keep
everything. A changed description rule is applied by the poll instead: stored rows carry the description rules hash,
and `DetailSelector` reads a known posting again when it differs.

## ChangeDetector

Pure function - no IO, no clock (it only borrows `VacancyMatcher`). Takes one board fetch, the global seen map, the
existing match rows and the subscriptions, and returns both levels at once: seen upserts and closures, match upserts
and removals, and the per-watchlist changes.

### Deduplication

One job can be posted many times (locations, languages). Posts are deduplicated by `{GroupId}|{Location}`,
case-insensitive. Posts without `GroupId` (prospect posts) always pass through - they have no group to collapse.

### Storage level

A fetched vacancy is stored when it matches at least one of `StorageFilters`. A stored row that is still listed but
matches none of them any more is **dropped** (`DroppedPostIds` - deleted, as `FilterMaintenanceService` does), not
closed: it did not leave the board, and a closure would count in `BoardActivity`.

### New / Updated (per watchlist)

Lookup is in the match rows of that watchlist, by `PostId`. Missing means `New`; present with a different
`ContentHash` means `Updated`. The hash is recomputed here, so a source that bumps its own `UpdatedAt` on cosmetic
edits produces nothing. A board added to a second watchlist reports its vacancies as `New` for that watchlist only -
the first one is not disturbed.

### Closed

Computed only when `Traverse.IsComplete` (the orchestrator already bails out earlier - this is a second guard).
`Seen` holds only open vacancies of the board, so anything in `Seen` that the board no longer lists is closed.

Closed is computed on both levels: globally (the post is no longer listed on the board) and per watchlist (the post
no longer passes that watchlist's filter). Per watchlist the removal is reported by its reason: `Closed` when the
post is gone from the board, `AgedOut` when it is listed but older than `PostedWithinDays`, `Filtered` when it is
listed and recent - the filter (or the posting) changed and rules it out.

Two consequences worth remembering:

- a vacancy that stops matching a watchlist filter is reported as `Filtered` to that watchlist and stays alive for
  the others - except when it is older than `PostedWithinDays`: that is `AgedOut`. Judged by the date alone,
  because a source skips the detail of a posting its date rules out, so the rest of the filter (a description above
  all) could not be checked anyway;
- a vacancy marked `DescriptionUnavailable` (its detail request failed) keeps its previous verdict per watchlist: it
  stays in a watchlist it matched, and cannot enter one it did not;
- the present-set is built from post-dedup vacancies plus the known rejected ones - a duplicate post that loses
  deduplication is closed.

The removed `Vacancy` is rebuilt from the stored row and reuses the reported `ContentHash`, so the dedup key stays
stable. When the stored row is already gone - `FilterMaintenanceService` runs before the poll and deletes rows no
filter keeps - a still-listed post is described by what the board shows now, so the removal is never silent.

## VacancyHasher

SHA-256 truncated to 32 hex chars. `Compute` hashes the fields listed in `VacancyExtensions.ToStringForHash`

## VacancyMatcher

Applies filter to a list of vacancies. `MayMatchListed` is the title and date parts alone - the checks a list-only
vacancy can fail for good, since location, description and (for Workday) the date may still come from a detail
request. Description rules are skipped for a vacancy marked `DescriptionUnavailable`.

## WatchService

Backs the bot: watchlist CRUD (create with an owner, claim the ownerless ones, rename, delete, enable/disable, filter,
interval), entry CRUD
(add/remove/enable/disable, mark worked through) and board lookup. `ListByOwnerAsync` is «my watchlists».
Authorization is not here - the bot is the only writer and `WatchlistAccess` in the sink project is the single
chokepoint that decides who may edit what. Everything goes through `IWatchlistStorage`, so a change is in
PostgreSQL the moment the command returns. Resolution itself lives in the source projects (`IBoardResolver`); this
service only orchestrates and filters.

`RemoveEntryAsync` returns an `EntryRemoveResult`: a manual entry is deleted, a discovered one is only **disabled**.
The disabled row is the memory of «the user does not want this board» - deleting it would let the next registry sweep
promote it right back. A later explicit `/board_add` of the same board flips its origin to manual, which is the
documented way to adopt a promoted board.

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
- If every candidate was dropped as already watched, the answer is `AlreadyWatched` and not `NotFound`. The check at
  the top of the method matches the text as typed, and one board has many urls (a careers page, a legacy portal url, a
  link to a single vacancy), so without this a second link to a board already in the list reads as «nothing found».

### AddAsync

An entry is unique per `(watchlist, source, board)`; re-adding a board refreshes its company name and re-enables it
instead of creating a second row. The same board in another watchlist is a separate entry, on purpose.
After a successful add `IPollingTrigger.RequestImmediateRun` wakes the polling loop - a board with no run stamp is due
at once.

## WatchlistStatsCalculator / WatchlistStatsService

The statistics of one watchlist for a period (`WatchlistStats`). The calculator is a pure function over the event
history (`WatchlistEvent`) and the board activity; the service reads both. `ComputeAsync` is the last N days ending
now (the bot, the digest); `ComputeRunAsync` is one run - its window, and with a run id only the events that run
committed (`WatchlistEvent.RunId`), so a job walking at the same time does not leak into the report.

- **opened / closed** - `New` / `Closed` events inside the period. `AgedOut` and `Filtered` are not closures.
- **new companies** - companies that had no open matching vacancy when the period began and got a `New` inside it.
  «Open at a moment» is the whole history replayed up to it (every event, not only the counted ones): the last event
  of a post being `New` means open.
- **emptied companies** - a `Closed` inside the period and nothing open at its end.
- **top by opened** - the most `New` events inside the period, top 3.
- **top by activity** - `BoardActivity.Events` since the period start (`IStateStore.CountBoardActivityAsync`), top 3,
  among the enabled boards of the watchlist only - activity is global to a board.

Companies are counted **by name**: the current entry name, falling back to the name the last event was reported
under. One company watched through two boards (two AstraZeneca sites) is one company, its numbers summed.

**Disabled companies are left out of every number** - their events are dropped from the period (the activity top
ranks enabled boards only anyway). A board that has left the watchlist still counts under its reported name.

## DigestService

`SendAsync` sends every enabled watchlist with at least one company its statistics for the last `Digest:PeriodDays`,
through `IReportSink`. Run by `--role digest` - the cadence (every 3 days) belongs to the scheduler, so nothing here
decides whether a digest is due and every call sends. A failed delivery is logged and the rest go on.

## RunReportService

The report a one-shot job sends after its drain. `SendTraversalAsync` - polling and registry: per enabled watchlist
with companies, what the run walked (`CycleReport`, null when it stopped early; its `ChangesSince` is the period the
changes cover, next to the run's own start and end) plus `ComputeRunAsync` of its run.
`SendDiscoveryAsync` - what a discovery run mined (`BoardDiscoveryReport`). Both are off with `Digest:RunReports`.

## WatchlistHistoryRepair

`--role historyrepair`: restores the closures the history is missing from `seen_vacancy`. The history began with the
open matches only, and jobs on older code did not write it, so closures were lost although `closed_at` remembers them.
A closed row of a watchlist board that passes the watchlist filter without its description and freshness rules
(descriptions are not stored; a vacancy is judged by its date when it is matched, not when it closes) becomes a
`Closed` at `closed_at` - after a `New` at `first_seen_at` when the post has no history at all. A post whose history
already ends with a removal gets nothing, so a second run adds nothing. An open vacancy is restored only from the match
layer (`LoadMatchedVacanciesAsync`), which knows exactly what the watchlist matches: a current match without any
history gets a `New` at `first_seen_at`. Run it once more after a deploy to fill what older jobs left unrecorded.

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

## TraversalProgressTracker

The counters behind `ITraversalProgressTracker`: what the two cycles are doing right now and how much of their dataset
they have walked, per source. The cycles already counted all of this, but only into the log, so «how far has the walk
got» was not answerable from outside - the admin screen reads it from here instead.

One `lock` over a dictionary of a few integers per source: `UnitFinished` is called from every concurrent board task
of a cycle, and a lock is nothing next to the fetch that just finished. A board of a source the plan never mentioned
is added on the fly - config may have drifted.

In-memory and process-wide, but only the *live* half of it is: `IsRunning` and the per-cycle counters start empty
after a restart, while coverage is computed from `board_poll_state` and therefore survives one. That distinction is
the whole reason the table exists - the tracker used to mirror in-memory scheduling fields, so a restart reported
`0 of N boards` and the walk really did begin again.

- `PollingOrchestrator` reports the whole watchlist board set as its dataset, the boards carrying a
  `board_poll_state` stamp as the covered part and the due boards as the plan of the cycle. Coverage is sent again
  after the stamps are written, so it is the post-cycle truth, and an empty or not-due cycle still closes the
  progress - otherwise the screen would read «still running» forever.
- `RegistryPollingService` reports the active, unwatched registry as its dataset and the slice as the plan. Covered
  there means «stamped within one walk», and the walk length is **measured**: the boards stamped over the last day,
  extrapolated to the whole registry. It is not derived from `CycleIntervalMinutes`, because a one-shot job runs on
  a cron that option knows nothing about. During the first walk the number grows towards 100%; after it, it stays
  there and drops only when the sweep stalls or discovery adds unswept boards.

## DeliveryWindow

The delivery window - `Of(minutes)` and `Floor(time, window)`, epoch-aligned. Two things must agree on where a
window ends and they live in different projects: `OutboxDispatcher` holds a notification back until its window is
closed, `MessageFormatter` groups the batch by the same boundary. Keeping the floor in one place is what rules out
the failure the split caused before it existed - a window delivered in halves, every half carrying the same header.

`TakeWholeWindows` applies the same boundary to a capped batch: given up to `max + 1` items oldest first, it keeps the
first `max` minus the window the cap cuts through (the extra item is how the cut is detected), so a batch never
carries half a window. Only a single window bigger than the cap is taken in parts.

## DetailSelector

For sources whose list lacks descriptions and part of the hashed fields (SmartRecruiters, Workday), decides per
posting whether the per-posting detail endpoint is asked (`DetailDecision`):

- `Fetch` for everything when `IncludeContentOnPoll` is set (the old, slow behaviour);
- `ListOnly` for a posting no storage filter can accept by title and publication date (`MayBeStored`);
- `Rejected` for a posting in `SourceTarget.Rejected` whose list data still has the remembered fingerprint
  (`ListHash` - `VacancyHasher` over the list-only mapping): the same filters rejected the same data before. The
  trade-off: a posting whose detail alone changes (its description) is not re-read until its list data or the filters
  change. Without it a posting failing a description filter was unknown on every poll - thousands of requests an hour;
- `Reuse` for a known posting whose list data did not move (the source's `ListUnchanged`) and whose verdict still
  stands: no description rule is in force, or its `DescriptionRulesHash` equals `SourceTarget.DescriptionRulesHash`
  (a null one, stored before the hash existed, counts as current). The stored vacancy supplies the detail fields, so
  the content hash stays put. A description rule change therefore costs one read of every stored posting of these
  sources, after which reuse resumes. Under a description filter the source marks it
  `DescriptionUnavailable`, so the description rules keep the verdict they gave when its text was last read. Before,
  every stored posting was re-read on every poll to confirm that verdict - about 3500 Workday requests per cycle;
- `Fetch` for every remaining posting when `NeedsDescription` - descriptions are not stored, so a new or changed
  plausible posting needs one, and without a budget: left list-only it could never pass the description rule;
- `Fetch` for new or changed postings up to the source's `MaxDetailsPerPoll`, `ListOnly` past it - a big board seen
  for the first time finishes instead of timing out every cycle. Such a posting keeps its list-only fields until its
  list data changes; it is never backfilled.

`FetchAsync` runs the selected requests with `DetailConcurrency` in parallel. `Detailed` and `Rejected` stamp the
mapped vacancy with its `ListHash` (and `KnownRejected`), which is what `BoardProcessor` remembers rejections by.

## CurrentTraversalRun

The `traversal_run` id of the one-shot job this process runs, set by `JobRunner` for the walk. `StateStore` stamps it
on every history event it writes - that is how a run report tells its own changes from those of a job walking in
parallel. Null outside a job (the bot, the `all` role).

## PollingTrigger

Latching wake-up signal between `WatchService` and the polling routine. `RequestImmediateRun` is a no-op when a
request is already pending, so repeated adds do not queue extra cycles; a request raised while the cycle is running
is not lost - the next `WaitAsync` returns immediately. `WaitAsync` returns on the wake-up or after the period,
whichever comes first. `IsRemote` is false: the cycle runs in this process. `IPollingTrigger.IsRemote` is true for the
host's `GitHubWorkflowTrigger`, whose cycle runs in GitHub Actions.

# Abstractions

## IStateStore

Responsible for atomic updates of seen vacancies, of the watchlist match layer and for enqueueing outbox
notifications - all in one transaction, so a notification can never exist without the state that produced it.
`LoadMatchedVacanciesAsync` is the feed the bot shows when a user opens a watchlist - the match layer joined to its open
`seen_vacancy` rows, newest first, capped at a limit. It is not database-paged on purpose: the bot groups the feed by
company and pages it by message size, which needs the whole set; the cap is what keeps a match-everything watchlist from
loading everything (`CountMatchesByWatchlistAsync` only gives the totals).
`CountOpenByBoardAsync` and `CountMatchesByBoardAsync` are the two halves of the company list the bot renders: how many
vacancies a board has at all, and how many of them match one watchlist - counts, so a screen listing 200 companies does
not load their feeds.
`CountBoardActivityAsync` is the third such read: how much *moved* on every board since a point in time - see
`BoardActivity`. It is counted from `seen_vacancy` rather than from `outbox`, which is purged within a day and
therefore remembers nothing.
`LoadAllAsync` and `PurgeAllAsync` are admin operations exposed through bot commands, not used by the pipeline;
`PurgeAllAsync` wipes derived state (vacancies, matches, outbox, registry) and keeps the watchlists, which are
configuration.

## IOutboxStorage

The notification queue. `ReadAndLeaseAsync(max, createdBefore, window)` takes a **cutoff** rather than just a size:
an item enqueued into the delivery window still being filled must stay pending, or the window is sent in pieces.
Reads are oldest-first and cut back to **whole windows** (`DeliveryWindow.TakeWholeWindows`): when the cap falls
inside a window, that window waits for the next batch - only a single window bigger than the cap is sent in parts.
The rest is retry bookkeeping - lease, deliver, fail with a backoff, dead-letter, purge.

## IBotUserStorage

The people using the bot (`bot_user`): the telegram user id a watchlist owner is stored as, the chat to deliver to, the
display name shown as the owner, and the interface language. `UpsertOnContactAsync` runs on every incoming update and
refreshes the chat id, the name and the last-seen stamp - but never the language, which is a setting only the user
changes. `GetManyAsync` resolves the owners of a whole listing in one query.

## IWatchlistStorage

The watchlist configuration: enabled watchlists with entries and filters for the pipeline, plus the CRUD the bot
needs. The only source of truth - there is no in-memory copy and no config-file fallback.

`ClaimOwnerlessAsync` hands every `owner_user_id IS NULL` watchlist to one user - the way a system watchlist from the
legacy import becomes an ordinary, fully editable one. The bot calls it for the administrator, because only an incoming
update reveals the telegram user id a migration would have needed.

`AddEntryAsync` and `AddDiscoveredEntryAsync` differ on exactly one point and it matters: the manual one refreshes and
re-enables an existing entry (and marks it manual), the discovery one refuses to touch an existing row at all -
enabled or disabled - and returns null. That is what keeps a dropped board dropped.

## IWatchlistEventStorage

Reads the history of changes reported to a watchlist (`watchlist_event`), oldest first, up to a moment. It is written
by `IStateStore.CommitAsync` in the same transaction as the outbox, so the history holds exactly the notifications
that were enqueued. Updates are not kept - they never change whether a vacancy is open. `AppendAsync` is for
`WatchlistHistoryRepair` only.

## IReportSink

Delivers the digest and the run reports: a watchlist's to wherever its notifications go, a discovery report to the
administrators.

## IVacancySink

Sink implementations must implement formatting and sending.

## IBoardResolver

Searching board via human-readable name - bot command /watch {company_name}.
`ProbeAsync` is also the validation step of board discovery - a token exists only if the ATS answers for it.

A resolver may return several candidates when the answer is genuinely ambiguous, which is what `LookupAsync` already
renders as a choice.

## IBoardUrlParser

ATS-specific knowledge for crawl index mining: which url patterns to ask the index for and how to read a board id
out of a captured url. Implemented in the source projects, consumed by `JobsPulse.Discovery`.

## IBoardRegistryStorage

The accumulative registry of boards known to exist (`board_registry`) plus the processed crawl indexes
(`crawl_index_state`). Independent from the watchlists: the registry is what exists, a watchlist is what we watch.
`CountBySourceAsync` and `CountProcessedCrawlsBySourceAsync` are the two aggregate reads the admin progress block is
built from.

## IBoardDiscoveryService

Fills the registry. Implemented in `JobsPulse.Discovery`; Core only holds the contract so the bot does not depend
on the discovery project. `GetProgressAsync` answers how much of the crawl dataset is mined - published indexes against
the ones recorded per source - and never throws: an index that does not answer is reported as «total unknown», because
the number is nice to have and not worth failing a screen over.

## IDiscoveryCheckpointStorage

The discovery offset (`discovery_checkpoint`), one row per iteration: `GetLatestAsync(count)` newest first and
`SaveAsync`, an upsert on the iteration number - the same row is rewritten every few minutes while a run walks.
Kept apart from `IBoardRegistryStorage` because it answers a different question: that one records which crawl
indexes are mined, this one where the current walk stands and what it has accumulated. Implemented in Storage,
written by `DiscoveryCheckpointTracker` in the discovery project.

## IBoardPollStateStorage

When each board was last polled (`board_poll_state`): `LoadAsync` gives the whole map keyed `{source}/{board}`
case-insensitively, `StampAsync` writes a batch of `BoardPollStamp` in one upsert. Deliberately the only
scheduling state of the pipeline, and deliberately not part of `IStateStore`: it is written *outside* the commit
of a board, after the whole cycle, so a poll that produced no state change still counts as a poll.

The map is loaded whole rather than queried per board - a cycle needs every board's stamp to sort and to report
coverage anyway, and the table has one narrow row per registry board.

## IRejectedPostingStorage

Postings the storage filters rejected after their detail was read (`rejected_posting`) - the counterpart of
`seen_vacancy` for what was not stored. `LoadAsync` gives a board's rows whatever filter set wrote them (the caller
keeps the current one), `SaveAsync` writes a difference. A filter change invalidates every row through its
`FilterHash`, so nothing has to purge them.

## IJobRunHistoryStorage

The history of the one-shot jobs (`job_run_history`): `StartAsync` when a job begins, `FinishAsync` with its outcome,
error and summary when it ends, `ListRecentAsync` for the bot. Kept for 30 days. Not `ITraversalRunStorage`: that one
coordinates deliveries between jobs in flight and forgets a run a day after it ended.

## ITraversalProgressTracker

Live progress of the two polling cycles - see `TraversalProgressTracker` for why it exists and what «covered» means
for each of them. In-process only: it knows nothing of a job on another runner, and it is idle between two slices of
a registry sweep - see `ITraversalRunStorage`.

## ITraversalRunStorage

One-shot jobs in flight across processes (`traversal_run`): `StartAsync` / `HeartbeatAsync` / `FinishAsync` by the
job, `AnyActiveAsync(aliveSince)` by the outbox cutoff. The polling and the registry jobs run on separate runners
and share one outbox, so «nothing is walking any more» has to be answered by the database. A row whose heartbeat is
older than `aliveSince` is a killed runner, not a walk; rows older than a day are dropped by the next start.

# Model Infrastructure

## FilterSpec

Filter specification. Three fields of a vacancy are filtered, each from both sides, and every one of the six lists is
editable from the bot (`FilterScreen`): `Title`, `Location` and `Description`, `AnyOf` (a hit on any value is enough) and
`NoneOf` (a hit drops the vacancy). Exclusions are evaluated first in `VacancyMatcher` - «not this» outranks «any of
these» - and `LocationAnyOf` is the one rule that also looks at `Offices`, because that is where several ATS keep the
place.

The description rules are the odd pair: `Vacancy.Description` is never persisted (it is too large and `[JsonIgnore]`d),
so they hold only while the vacancy is being polled. Two consequences the bot states in its prompts - a vacancy whose
text could not be read never passes `DescriptionAnyOf` and always passes `DescriptionNoneOf`, and
`FilterMaintenanceService` drops both from the filter copy it re-evaluates stored rows with.

### PostedWithinDays

Truncate old vacancies (null - no truncation). Age is `FirstPublishedAt` (the board's date), falling back to
`FirstSeenAt` only for a source that reports none: `FirstSeenAt` is our own stamp, so judging by it made every old
vacancy of a board look new on its first poll.

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

## BotUser / BotLanguage

One person talking to the bot. The telegram user id is the identity - it owns watchlists and survives a chat being
recreated, which a chat id does not. `BotLanguage` (`English` / `Russian`) is stored per user, so it applies to the
notifications that arrive hours after the switch, not just to the current screen.

## WatchlistEntry.WorkedAt

When the user marked a company as worked through - a CV went out. A nullable stamp rather than a flag, because the date
is what they want to see when coming back to the list later. `IsWorked` is the shorthand.

## BoardOrigin

Who put a board into a watchlist: `Manual` (the bot) or `Discovery` (promoted from the registry by
`DiscoveredBoardPromoter`). Carried by `WatchlistEntry.Origin` and, denormalized, by `OutboxItem.Discovered` - a
delivered message must stay readable after the entry is gone, the same reasoning as `OutboxItem.WatchlistName`.

Storage returns entries ordered origin-first, so every listing shows manual boards before discovered ones without
sorting again.

## TraversalKind / TraversalSourceUnits / TraversalProgress

The vocabulary of the progress tracker. `TraversalKind` is which dataset is walked (`Watchlist`, `Registry`);
`TraversalSourceUnits` is one source of a cycle - what it planned and how big its dataset is - and serves as both the
plan a cycle announces and the coverage it reports back; `TraversalProgress` (with `TraversalSourceProgress`) is the
snapshot a reader gets, totals and percentages included. `Percent` treats an empty dataset as complete: nothing to do
is done, not zero.

## JobRun / JobRunOutcome / JobRunSummary / JobRunRoles

One row of the job run history. `JobRunOutcome`: `Running`, `Succeeded`, `Failed`, `TimedOut` (reached
`Job:MaxRunMinutes` - the next run continues, not a failure) and `Stopped` (cancelled from outside).
`JobRunSummary` holds the numbers of a run, every one optional - each job fills what means something for it.
`JobRunRoles` are the role names the host records runs under.

## BoardActivity

How much moved on one board inside a window - vacancies opened, changed and closed - plus the length of that window
in months, so `PerMonth` is comparable between boards. Read from the three stamps `seen_vacancy` already carries
(`first_seen_at`, `updated_at`, `closed_at`) in one grouped query, so the indicator needs no new table and no history
of its own.

It is deliberately a count of **events, not posts**: a vacancy that opened and then changed inside the window counts
twice. A board that keeps rewriting its postings is exactly as interesting to a reader as one that keeps adding them -
and an implausible rate is usually a source bug, which is the second thing the number is good for. Global to the
board, not per watchlist: `seen_vacancy` is the shared level.

## DiscoveryProgress / DiscoveryCheckpoint

`DiscoveryProgress` is how much of the crawl dataset is mined - see `IBoardDiscoveryService.GetProgressAsync` - plus
the `Current` and `Previous` iterations, which is what lets the admin screen say how the running walk compares to
the last one.

`DiscoveryCheckpoint` is one iteration: its ordinal (`Iteration` - how many discovery runs the installation has ever
started), whether it is a bootstrap, where it started (`StartedFromCollectionId`), where a restart continues from
(`ResumeFromCollectionId`, null once the window is behind it) and the counters accumulated so far. `CollectionsDone`
counts what the offset has moved past whatever the outcome, `CollectionsProcessed` only the ones that were mined
whole. `UpdatedAt` is when the offset was last written, so «how stale is this» is answerable from the record alone.

## BoardPollStamp

«This board was polled at this moment» - the unit `IBoardPollStateStorage.StampAsync` takes. A
`readonly record struct`, because a cycle builds a few hundred of them per sweep and they live for one call;
`BoardKey` renders the same `{source}/{board}` string the poll-state map is keyed by.

## RegistrySweepState

The snapshot a registry sweep walks: enabled watchlists, their plan, the registry boards worth polling and the poll
map. Loaded once per sweep by `RegistryPollingService`; the poll map is updated in memory as slices are stamped.

## WatchlistEvent

One change reported to one watchlist, kept after its outbox row is purged: post, company name as reported, kind
(`New`, `Closed`, `AgedOut`, `Filtered` - `IsTracked`), where the vacancy is (`Vacancy.LocationOrOffice` - the
location, or the first office when the board names none; recorded, not shown yet), when, and the run that committed it (`RunId`, null outside a
job and for restored history). The history the statistics are replayed from.

## WatchlistStats / CompanyCount / CompanyActivity

The statistics of one period - see `WatchlistStatsCalculator`. `CompanyCount` and `CompanyActivity` are the rows of
its two top lists. `Days` is 0 for a run report, which is not measured in days.

## TraversalRunReport / DiscoveryRunReport

What `RunReportService` hands the sink: the kind of traversal, its `CycleReport` and the run's `WatchlistStats`; or
the window, the bootstrap flag and the `BoardDiscoveryReport` of a discovery run. A report of a run that stopped
early carries null instead of the counts.

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

### DescriptionUnavailable

Set by a source when the detail request of a stored (`SourceTarget.Known`) posting failed while a filter reads
descriptions. The vacancy then carries the stored fields and no description; `VacancyMatcher` skips description rules
and `ChangeDetector` keeps its previous watchlist verdict, so a transient HTTP error neither closes nor adds a match.
Not persisted, not serialized.

### DescriptionRulesHash

Which description rules the stored verdict was given under (`seen_vacancy.description_rules_hash`), stamped by
`BoardProcessor`. `DetailSelector` reuses a known posting only while it equals the current one. Not serialized.

### ListHash / KnownRejected

Set by `DetailSelector` for a list-only-mapping source: `ListHash` is the fingerprint of the list data, present when
the detail was read or skipped as known rejected; `KnownRejected` marks the latter - a list-only vacancy that
`ChangeDetector` neither stores nor matches. Not persisted, not serialized.

## OutboxItem

### Id

Incremental LONG id.

### WatchlistId / WatchlistName

Which watchlist the notification belongs to, denormalized so a delivered message stays readable after the watchlist is
renamed or deleted. Null only for synthetic items (the `/show_state` dump).

### CreatedAt

When the change was detected and enqueued, not when it is delivered. The telegram sink buckets a batch by it, so
everything one cycle found within `Delivery:GroupChangesWithinMinutes` reads as a single message.

### DedupKey

- Idempotency key. Single change won't be enqueued twice.
- Format: {Vacancy.Key}|{WatchlistId}|{ChangeKind}|{ContentHash} - the watchlist is part of the key because the same
  vacancy legitimately produces one notification per watchlist.

# Options

- `WatchlistPollingOptions` - section `WatchlistPolling`: `PollingIntervalMinutes`, `MaxConcurrentEntries`,
  `SingleEntryProcessTimeoutSeconds`, `DryRun`. The keys must match the property names: `appsettings.json` once
  carried a `Polling` section with other names, and nothing in it was ever applied.
- `RegistryPollingOptions` - section `RegistryPolling`, see the class.
- `DeliveryOptions` - section `Delivery`, see the class.
- `DigestOptions` - section `Digest`: `PeriodDays` (3 - the period of `--role digest`; `digest.yml` passes its
  input here), `MaxPeriodDays` (365 - the longest period the bot computes on request), `RunReports` (true - a report
  after every polling, registry and discovery run).
