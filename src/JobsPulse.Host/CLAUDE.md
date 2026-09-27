# Program

`--role <name>` (`HostRole`) picks what the process runs:

- `all` (default) - every routine in one long-living process, the local development setup;
- `bot` - only `TelegramBotListener`; no traversal and no outbox dispatch. With `GitHubDispatch:Token` set,
  `IPollingTrigger` is `GitHubWorkflowTrigger`;
- `webhook` - the same bot served over HTTP (`TelegramWebhookEndpoint`) on a `WebApplication` listening on `$PORT`
  (8080 by default), for Cloud Run: scales to zero between messages, one instance at most, console log only.
  Deployed by `.github/workflows/deploy-bot.yml`;
- `webhooksetup` - one-shot: `WebhookRegistrar` points Telegram at `TelegramWebhook:PublicUrl` and publishes the
  command menu. The deploy workflow runs it in the freshly built image;
- `polling`, `registry`, `discovery`, `cleanup` - one-shot jobs run by `JobRunner`, scheduled by the GitHub Actions
  workflows in `.github/workflows` (`_run-job.yml` builds and runs; the others are started by cron-job.org through `workflow_dispatch` and hold no `schedule:` - GitHub fired it late and irregularly). The process exits with
  the job's code; SIGINT/SIGTERM from a cancelled workflow stop it gracefully. After every run, whatever its result,
  `_run-job.yml` upserts the healthchecks.io check `jobspulse-<role>` (secret `HEALTHCHECKS_API_KEY`, read-write) with
  period = the caller's `cadence-minutes` and grace = twice that, then pings it - an email alert comes only when a job
  has not run for three cadences. Failures are left to GitHub's own failure emails.

Every role migrates the database first. Hosted services are registered here only - `AddBoardDiscovery` and
`AddTelegramSink` no longer register their workers. The repository root holds a `Dockerfile` (ASP.NET runtime,
ReadyToRun) that serves every role; its default is `bot`, Cloud Run overrides it with `--role webhook`.

# Infrastructure

## SourceCatalog

Resolves `IVacancySource` / `IBoardResolver` by source id out of the keyed DI registrations.

## LegacyWatchlistImporter

One-shot import of the retired `watchlist.json` into PostgreSQL: runs after the migration, only while there is no
watchlist at all, and creates a single watchlist named `default` with the old `defaultFilter` and entries. After that
the file is dead weight - the database is the only source of truth and the bot is the only way to change it.

## GitHubWorkflowTrigger

`IPollingTrigger` of the `bot` and `webhook` roles (`IsRemote`): `RequestImmediateRun` fires a `workflow_dispatch` of
`GitHubDispatch:Workflow`, at most once per `CooldownSeconds`. Failures are only logged. The workflow's concurrency
group queues the run behind a cycle that is already walking. `PendingAsync` is the dispatch still in flight - the
webhook endpoint awaits it, because Cloud Run throttles the CPU once the response is sent.

## TelegramWebhookEndpoint

`POST {TelegramWebhook:Path}` (`/telegram`) and `GET /healthz`. A request without the right
`X-Telegram-Bot-Api-Secret-Token` (compared in constant time) is 401, an unreadable body 400. The update is handled
before the response - the container has CPU only while a request is open - and a failing update still answers 200,
so Telegram does not redeliver it forever.

# Models

- `HostRole` - see Program.
- `OutboxDispatchResult` - delivered count of one dispatch tick; `RetryAfter` is set when the batch failed.

# Options

- `JobOptions` (`Job`) - `MaxRunMinutes` time-boxes a one-shot job (0 - none), `DrainTimeoutMinutes` bounds the
  outbox drain after it, `MaxDrainRetryAfterSeconds` is the longest delivery retry the drain waits out.
- `GitHubDispatchOptions` (`GitHubDispatch`) - `Token` (fine-grained PAT, Actions: write), `Repository`, `Workflow`,
  `Ref`, `CooldownSeconds`.

# Pipeline

## JobRunner

One iteration of a role: `polling` (filter maintenance + `RunCycleAsync`), `registry` (`TryRunSweepAsync` until
`Job:MaxRunMinutes`, a single `TryRunCycleAsync` without one),
`discovery` (`DiscoveryBootstrapPolicy` decides bootstrap vs incremental), `cleanup` (purge). Reaching
`Job:MaxRunMinutes` is a success - all routines keep their progress in the database.

`polling` and `registry` run the dispatcher loop (`DispatchOnceAsync` every `Delivery:DispatchOutboxIntervalSeconds`)
next to the cycle, so closed delivery windows leave while the walk goes on. The loop is stopped between ticks, never
mid-delivery. Afterwards the outbox is drained, also after a failure or a deadline, so committed changes are not
held until the next run.

## OutboxDelivery

The outbox logic shared by `OutboxDispatcher`, `OutboxCleanupWorker` and `JobRunner`.

### Flow of `DispatchOnceAsync`:

- Mark 'pending' outbox letters with exhausted attempts as 'dead'
- Compute the **cutoff** - which letters are allowed to leave (see below)
- Read and lease 'pending' outbox letters enqueued before it
- Send messages to sink
- Mark outbox letter
    - 'sent' on success
    - 'pending' on failure and reschedule retry (telegram response timeout or exponential backoff)
    - 'pending' when the sink throws or the delivery is cancelled - a leased letter is never picked up again

`DrainAsync` repeats it until nothing is left, waiting out retries up to the given limit. `PurgeDeliveredAsync`
deletes `Delivered` rows older than `Delivery:DeliveredRetentionHours`.

### The cutoff

The loop ticks every `Delivery:DispatchOutboxIntervalSeconds` (5), while a traversal commits **per board**. Leasing
whatever is pending on every tick therefore produced one message per company, each one stamped with the same
`Delivery:GroupChangesWithinMinutes` window header - the grouping `MessageFormatter` does was never handed anything
to group. `CutoffAsync` is the fix: a letter of the window still being filled stays pending.

Two things open the gate:

- the window closed (`DeliveryWindow.Floor`, `Delivery:GroupChangesWithinMinutes` = 15) - the ordinary case, and
  what makes the messages read as 15-minute ranges;
- **every traversal is idle** (`ITraversalProgressTracker`) - the walk is over, nothing more can land in the open
  window, so holding it back would only delay the report. `CycleFinished` is raised after the last commit of a
  cycle, which is what makes this safe.

There used to be a third one - «the open window already holds `FlushWindowAfterChanges` (50) letters, send it now»
- and together with a batch cap of 50 it was the source of «many messages for one window»: the first 50 changes left
early, the rest followed under the same header, and a closed window of 500 changes went out as ten batches. Now the
open window always waits, and a batch (`OutboxBatchSize`, 500) takes whole windows only.

The tracker is in-process, which is why the outbox is dispatched only by the process that walks: a one-shot job
dispatches next to its own cycle, and the `bot` role does not dispatch at all.

Two cycles can still interleave inside one window - the watchlist cycle ends and flushes, the registry sweep starts
a moment later and fills the same window - and that second report repeats the header. One message per cycle is the
floor here; the per-company flood is what the cutoff removes.

# Routines

Registered only in the `all` role.

## PollingWorker

Runs `PollingOrchestrator.RunCycleAsync` in a loop. Between cycles it waits on `IPollingTrigger` instead of a plain
delay, so a new watchlist entry starts a cycle immediately; overlapping runs are prevented by the orchestrator gate.

## RegistryPollingWorker

Drives `RegistryPollingService` every `RegistryPolling:CycleIntervalMinutes` after a start delay. Independent from
`PollingWorker`: the watchlist feed keeps its own cadence and is never blocked by the registry sweep.

## OutboxCleanupWorker

`OutboxDelivery.PurgeDeliveredAsync` every `Delivery:CleanupIntervalMinutes`. Only delivered rows are touched -
pending, leased and dead letters stay.

## OutboxDispatcher

`OutboxDelivery.DispatchOnceAsync` every `Delivery:DispatchOutboxIntervalSeconds`.
