Board discovery: mines web crawl indexes (Common Crawl) for ATS board urls and fills the accumulative board
registry (`board_registry` table, `IBoardRegistryStorage` in Core).

Nothing here knows about a particular ATS. Everything source-specific lives in the source project behind
`IBoardUrlParser` (`GreenhouseBoardUrlParser`), so adding Lever means adding one parser, not touching this project.

# Abstractions

## ICrawlIndexClient

Generic index reader:

- `GetCollectionsAsync` / `GetLatestCollectionAsync` - `collinfo.json`, newest first. This is how the freshest index
  is found without knowing its name, so the periodic run never guesses collection ids.
- `GetPageCountAsync` - `showNumPages=true`, the index reports how many pages a url pattern has.
- `StreamPageAsync` - one page as JSONL, streamed line by line; a page can hold hundreds of thousands of captures.

# Infrastructure

## CrawlIndexClient

Implementation over the CDX API. Requests are `output=json&fl=url,timestamp,status&filter==status:200` - only
successful captures, only the fields that matter. Unparsable lines are skipped, not fatal.

The index front-end throttles hard and answers 503/429 under load, so every request goes through a linear-backoff
retry (`IndexRetries`, `IndexRetryDelaySeconds`); a request that never succeeds yields an empty page instead of
killing the run.

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
- validated boards are upserted in batches, then the collection is marked processed

Runs never overlap - a zero-timeout `SemaphoreSlim(1, 1)`, the same trick as `PollingOrchestrator`. A busy service
returns `BoardDiscoveryReport.Busy`.

`MaxNewTokensPerRun` is a safety valve: the first bootstrap of `boards.greenhouse.io/*` can yield tens of thousands
of tokens, and every one of them costs two Greenhouse requests to validate. What is left over is picked up by the
next run, because the collection is marked processed only after it is fully scanned.

# Routines

## BoardDiscoveryWorker

Starts after `StartDelayMinutes`, then runs every `RunIntervalHours`. The very first run is a full bootstrap when
the registry is empty; everything after that is incremental.

# Options

`Discovery` section - see `DiscoveryOptions`.
