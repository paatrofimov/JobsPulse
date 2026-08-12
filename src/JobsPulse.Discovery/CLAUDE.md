Board discovery: mines web crawl indexes (Common Crawl) for ATS board urls and fills the accumulative board
registry (`board_registry` table, `IBoardRegistryStorage` in Core).

Nothing here knows about a particular ATS. Everything source-specific lives in the source project behind
`IBoardUrlParser` (`GreenhouseBoardUrlParser`, `LeverBoardUrlParser`, `SmartRecruitersBoardUrlParser`,
`AshbyBoardUrlParser`, `WorkdayBoardUrlParser`), so adding a source means adding one parser, not touching this project.

A pattern may name a whole domain (`*.myworkdayjobs.com/*`) instead of a known host. That is what Workday needs - every
tenant gets its own careers host - and it costs a suffix match where the other sources get equality, in both index
readers. A token whose board id the crawl could only guess (Workday's tenant) is not a special case either: validation
already probes every token against the ATS, and the candidate it returns is what the registry stores, so a corrected
address lands there instead of the guessed one.

# Modes

Common Crawl publishes the same index twice, and `Discovery:Mode` picks which one is read. It is a flags enum, so
both may be on.

- `Parquet` (default) - the columnar index: remote parquet files queried in place by DuckDB. One pass answers for
  every ATS at once.
- `Http` - the cdx api of `index.commoncrawl.org`. One source, one collection, one page at a time; the front-end
  throttles hard, which is why this is off by default.

Both passes share `crawl_index_state`, so whatever one of them has finished the other skips for free. That is also
what makes `Parquet:FallbackToHttp` cheap: when the columnar reader leaves collections pending, the http pass is run
after it and picks up exactly those.

# Abstractions

## ICrawlIndexClient

Generic index reader over the cdx api:

- `GetCollectionsAsync` / `GetLatestCollectionAsync` - `collinfo.json`, newest first. This is how the freshest index
  is found without knowing its name, so the periodic run never guesses collection ids. Cached for
  `CollectionsCacheMinutes` - the list changes a few times a year. Used by both modes: the parquet reader needs the
  crawl ids too.
- `GetPageCountAsync` - `showNumPages=true`, the index reports how many pages a url pattern has.
- `StreamPageAsync` - one page as JSONL, streamed line by line; a page can hold hundreds of thousands of captures.

`GetPageCountAsync` and `StreamPageAsync` throw `CrawlIndexUnavailableException` when the index never answered.
That is the point: «zero pages» and «no answer» must not look the same to the caller, or a collection nobody could
read would be marked processed.

## ICrawlIndexFileCatalog

Which parquet files one crawl consists of. The metadata step of the columnar index and the only thing about it that
is fetched over plain http.

## IParquetIndexClient

The columnar reader. Two calls, both throwing `ParquetIndexUnavailableException` when nothing answered:

- `ProbeFilesAsync` - narrow a file set down to the files that hold anything for the targets.
- `ScanAsync` - the board urls themselves, handed to a callback row by row.

# Infrastructure

## CrawlIndexClient

Implementation over the CDX API. Requests are `output=json&fl=url&filter==status:200` - only successful captures,
only the field that matters. Unparsable lines are skipped, not fatal.

A `*.domain/path/*` pattern is trimmed to `*.domain` before it is sent: the cdx api reads a leading `*.` as a whole
domain, but only when nothing follows the host. The path filter is lost, so every capture of the domain is streamed and
the parser is what rejects them - which it does anyway.

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

## CrawlIndexFileCatalog

Reads `crawl-data/{crawl}/cc-index-table.paths.gz` - two kilobytes listing every parquet file of a crawl (900 of
them, split evenly between the `warc`, `robotstxt` and `crawldiagnostics` partitions) - and keeps only the
`Parquet:Subset` one. Cached per crawl id: a published listing never changes.

## ParquetIndexClient

DuckDB with `httpfs` over one in-memory database. The files are queried where they live, so only the footers and the
column chunks that survive the predicate cross the network - nothing is downloaded and nothing is written to disk.

Queries are serialized by one gate, the same way the http client paces its requests. The data host throttles range
requests exactly like the cdx front-end (a 300-file query answers `HTTP 503` under load), so a failure is the normal
path: `Parquet:Retries` linear steps of `RetryDelaySeconds * attempt` capped by `MaxRetryDelaySeconds`, and every
attempt starts from a fresh connection because a broken one stays broken. `SET` statements that a DuckDB version
does not know are logged and skipped, not fatal.

The reader is synchronous, so a query runs on a pool thread and rows are handed to the caller as they arrive - a
result set of any size costs one row of memory here.

## ParquetIndexSql

Where the cost of the columnar index is actually decided, and the reasoning is worth keeping:

The Common Crawl files carry min/max statistics for exactly one column worth having - `url_host_tld`. Neither
`url_surtkey` (which the table is sorted by) nor `url_host_name` has any, so a range predicate on the sort key
prunes nothing. What is left to exploit is column width, and the widths differ by orders of magnitude:
`url_host_tld` is a handful of values, `url_host_name` a few million, `url_path` is unique per row.

Hence two queries instead of one. `Probe` narrows the file set on a cheap column; `BoardUrls` pays for the wide ones
on the few files that are left, cuts the posting id off the path (`UrlPathSegments` leading segments) and returns
`DISTINCT`, so a board with 5000 job pages comes back as one row. Every ATS is one `OR` group of the same query.

A whole-domain target is `url_host_name LIKE '%.domain'` instead of equality. The leading `%` rules out any row group
skip, which is exactly why it is per-pattern and not the general case - but for an ATS whose host is per tenant there is
no host to ask for by name, so the alternative is not asking at all.

Measured on `CC-MAIN-2025-30`: 300 files → 137 after the tld probe (2m40s) → 4 after the host probe (3m30s) → 36k
distinct urls in 48s, which is ~7 minutes against the ~2 hours a single wide query over all 300 files costs.

## BoardIndexTargets

Translates the cdx url patterns a source already declares (`boards-api.greenhouse.io/v1/boards/*`) into columnar
index targets - tld, host, path prefix. Tld and path are plain equality, so no public suffix or surt
canonicalization assumption can silently drop a board, and adding an ATS still means adding one `IBoardUrlParser`.

A leading `*.` (`*.myworkdayjobs.com/*`) is a host *mode*, not a path wildcard: the target is marked `HostIsSuffix` and
matches every subdomain of the domain. Only an ATS that gives each tenant its own host needs it - the predicate cannot
prune on a `LIKE '%.domain'`, so it is deliberately not the default.

## HostParserIndex

Which ATS a host belongs to, asked once per url out of millions: a dictionary for the exact hosts and a walk over the
few whole-domain targets. Kept apart from the passes because both readers need the same answer.

## CrawlIndexFailure

`IsTransient` / `Describe` - one place that decides «the index did not answer» versus «the code is broken», used by
both clients and both passes. `OperationCanceledException` is never transient, so a shutdown always propagates.

## StageTimer / DiscoveryPause / DiscoveryReports

A discovery stage takes minutes and produces nothing until it is over, so the log is the only progress bar there is:
`StageTimer` announces a stage and reports how long it took (`Outcome` replaces the closing word, so a stage that
gave up does not read as one that succeeded). `DiscoveryPause` is the polite pause between two units of work,
`DiscoveryReports` adds the per-collection outcomes up.

## DiscoveryServiceCollectionExtensions

`AddBoardDiscovery(config)` registers both named HttpClients (`common-crawl-index` for the cdx api with a 10 minute
timeout - pages are streamed - and `common-crawl-data` for the parquet path listings), both index clients, both
passes, the token sink, `IBoardDiscoveryService` and the background worker.

# Pipeline

## BoardDiscoveryService

One `RunAsync(full)` call is one discovery pass over every registered `IBoardUrlParser`. This class only decides
which index is read and in what order; the reading is in the passes.

- `full: true` (bootstrap / `/discover`) - union of the last `BootstrapYears` of crawl indexes, processed marks are
  ignored, indexes are walked oldest first.
- `full: false` (periodic) - every collection that is not yet in `crawl_index_state` for that source, which in
  practice is only the newly published one. History is never re-read.

Runs never overlap - a zero-timeout `SemaphoreSlim(1, 1)`, the same trick as `PollingOrchestrator`. A busy service
returns `BoardDiscoveryReport.Busy`.

## ParquetIndexDiscoveryPass

The default reader, and collection-major rather than source-major: a query costs the same whether it asks about one
ATS or ten, so every parser is folded into one pass over the files. Per collection:

- resolve the parquet files (`ICrawlIndexFileCatalog`)
- tld probe → host probe → board url query, each batched by `Parquet:FilesPerQuery` so a throttled query costs one
  batch instead of the whole set, and each logged with its file counts and duration
- the host of a returned url picks the one parser that owns it, tokens already in the registry are dropped
- per source: validate, upsert, and mark the collection processed - but only if it was scanned whole

Sources already holding the collection in `crawl_index_state` are left out of the predicate, so a re-run after a
partial failure asks about less.

## HttpIndexDiscoveryPass

The cdx reader, source-major. For every pending collection and every url pattern: page count → stream pages → parse
board token.

## BoardTokenSink

The last stage of both passes: unknown tokens are probed against the ATS itself (`IBoardResolver.ProbeAsync`),
throttled by `ValidationConcurrency`, and the survivors are upserted in `UpsertBatchSize` batches. A probe failure
just drops the token. `DiscoveredVia` records which reader found the board - `common-crawl-parquet:{crawl}` or
`common-crawl:{crawl}`.

The row is built from the **candidate**, not from the token: the board id and the `Configuration` are the ones the probe
confirmed, so an ATS whose token carries a guess (Workday's tenant) is registered at its real address and with the
configuration that makes it addressable at all.

## Failures and progress

Index requests fail all the time, so a failure is never fatal for the run - it only decides what is skipped:

- http: a pattern whose page count fails is skipped; after `MaxPageFailuresPerCollection` failed pages the
  collection is abandoned
- parquet: after `Parquet:MaxBatchFailuresPerCollection` failed batches (probes and scans counted together) the
  collection is abandoned. A failed probe batch means some file was never looked at, so the collection stays pending
  even if every scan succeeded
- `MaxConsecutiveCollectionFailures` collections failing in a row → the pass is given up on, the rest stays pending

Tokens found before a failure are still validated and upserted - the upsert is idempotent, so nothing is lost by
storing them early.

`crawl_index_state` is written only for a `Completed` collection: no failed request, no truncation. A collection
that failed, or that was cut short by `MaxNewTokensPerRun`, stays pending and the next run walks it again - which is
the whole point of the state table. `MaxPagesPerCollection` and `Parquet:MaxFilesPerCollection` are the one
exception: a deliberate configuration cap would otherwise make the collection pending forever, so the skipped tail
is only logged.

`MaxNewTokensPerRun` is a safety valve: the first bootstrap of `boards.greenhouse.io/*` yields ~3000 tokens per
crawl, and every one of them costs two Greenhouse requests to validate. The budget is per source and per run, shared
by all collections; the collections left behind are reported as pending.

`BoardDiscoveryReport` counts `CollectionsProcessed` (marked processed), `CollectionsFailed` and
`CollectionsPending`, so the log line tells a finished run from a run that mostly fought the index.

# Routines

## BoardDiscoveryWorker

Starts after `StartDelayMinutes`, then runs every `RunIntervalHours`. The very first run is a full bootstrap when
the registry is empty; everything after that is incremental. The bootstrap flag is cleared only after a run that
actually started, so a failed first attempt is retried as a bootstrap.

# Options

`Discovery` section - see `DiscoveryOptions`, `DiscoveryMode` and the nested `Discovery:Parquet`
(`ParquetIndexOptions`).
