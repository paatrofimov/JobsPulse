Board discovery: mines web crawl indexes (Common Crawl) for ATS board urls and fills the accumulative board
registry (`board_registry` table, `IBoardRegistryStorage` in Core).

Nothing here knows about a particular ATS. Everything source-specific lives in the source project behind
`IBoardUrlParser` (`GreenhouseBoardUrlParser`, `LeverBoardUrlParser`, `SmartRecruitersBoardUrlParser`), so adding a
source means adding one parser, not touching this project.

# Abstractions

## ICrawlIndexClient

Generic index reader:

- `GetCollectionsAsync` / `GetLatestCollectionAsync` - `collinfo.json`, newest first. This is how the freshest index
  is found without knowing its name, so the periodic run never guesses collection ids. Cached for
  `CollectionsCacheMinutes` - the list changes a few times a year.
- `GetPageCountAsync` - `showNumPages=true`, the index reports how many pages a url pattern has.
- `StreamPageAsync` - one page as JSONL, streamed line by line; a page can hold hundreds of thousands of captures.

`GetPageCountAsync` and `StreamPageAsync` throw `CrawlIndexUnavailableException` when the index never answered.
That is the point: «zero pages» and «no answer» must not look the same to the caller, or a collection nobody could
read would be marked processed.

# Infrastructure

## CrawlIndexClient

Implementation over the CDX API. Requests are `output=json&fl=url&filter==status:200` - only successful captures,
only the field that matters. Unparsable lines are skipped, not fatal.

The index front-end throttles hard: it answers 503/429 and simply drops connections under load
(`SocketException 10054`), so failing is the normal path and the client is built around it.

- Every request goes through one gate, so pacing is global: at least `PauseBetweenRequestsMsec` between any two
  requests, plus the current throttle penalty.
- The penalty grows by `ThrottlePenaltyStepSeconds` (up to `MaxThrottlePenaltySeconds`) on every throttled or failed
  request and is relaxed by one step after `ThrottleRecoveryAfterRequests` requests in a row succeed. Both
  directions are logged, so the log shows how hard the index is pushing back.
- Retries are `IndexRetries` linear steps of `IndexRetryDelaySeconds * attempt`, honouring `Retry-After` when it
  asks for more, capped by `MaxIndexRetryDelaySeconds`.
- A non-transient status (404 for a pattern with no captures) is returned as-is - retrying it would be waste.
- A body cut off mid-stream is a failed page, not an empty one.

## CrawlIndexFailure

`IsTransient` / `Describe` - one place that decides «the index did not answer» versus «the code is broken», used by
both the client and the pipeline. `OperationCanceledException` is never transient, so a shutdown always propagates.

## DiscoveryServiceCollectionExtensions

`AddBoardDiscovery(config)` registers the named HttpClient (10 minute timeout - pages are streamed), the client,
`IBoardDiscoveryService` and the background worker.

# Pipeline

## BoardDiscoveryService

One `RunAsync(full)` call is one discovery pass over every registered `IBoardUrlParser`.

- `full: true` (bootstrap / `/discover`) - union of the last `BootstrapYears` of crawl indexes, processed marks are
  ignored, indexes are walked oldest first.
- `full: false` (periodic) - every collection that is not yet in `crawl_index_state` for that source, which in
  practice is only the newly published one. History is never re-read.

### Flow per source

- read processed crawl ids and known board ids
- for every pending collection and every url pattern: page count → stream pages → parse board token
- tokens already in the registry are dropped before validation - dedup happens against the whole registry, not just
  within the run
- unknown tokens are validated against the ATS itself (`IBoardResolver.ProbeAsync`), throttled by
  `ValidationConcurrency`; a probe failure just drops the token
- validated boards are upserted in batches, then the collection is marked processed - but only if it was scanned
  whole

Runs never overlap - a zero-timeout `SemaphoreSlim(1, 1)`, the same trick as `PollingOrchestrator`. A busy service
returns `BoardDiscoveryReport.Busy`.

### Failures and progress (`CollectionScanResult`)

Index requests fail all the time, so a failure is never fatal for the run - it only decides what is skipped:

- page count of a pattern fails → the pattern is skipped, the next one is tried
- a page fails → the next page is tried; after `MaxPageFailuresPerCollection` failed pages the collection is
  abandoned and the next collection is taken
- `MaxConsecutiveCollectionFailures` collections failing in a row → the source is given up on for this run, the rest
  stays pending

Tokens found before a failure are still validated and upserted - the upsert is idempotent, so nothing is lost by
storing them early.

`crawl_index_state` is written only for a `Completed` collection: no failed request, no truncation. A collection
that failed, or that was cut short by `MaxNewTokensPerRun`, stays pending and the next run walks it again - which is
the whole point of the state table. `MaxPagesPerCollection` is the one exception: a deliberate configuration cap
would otherwise make the collection pending forever, so it is marked processed and the skipped tail is logged.

`MaxNewTokensPerRun` is a safety valve: the first bootstrap of `boards.greenhouse.io/*` can yield tens of thousands
of tokens, and every one of them costs two Greenhouse requests to validate. The budget is per run and shared by all
collections of a source; the collections left behind are reported as pending.

`BoardDiscoveryReport` counts `CollectionsProcessed` (marked processed), `CollectionsFailed` and
`CollectionsPending`, so the log line tells a finished run from a run that mostly fought the index.

# Routines

## BoardDiscoveryWorker

Starts after `StartDelayMinutes`, then runs every `RunIntervalHours`. The very first run is a full bootstrap when
the registry is empty; everything after that is incremental. The bootstrap flag is cleared only after a run that
actually started, so a failed first attempt is retried as a bootstrap.

# Options

`Discovery` section - see `DiscoveryOptions`.
