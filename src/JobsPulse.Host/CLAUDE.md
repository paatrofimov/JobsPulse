# Infrastructure

## SourceCatalog

Resolves `IVacancySource` / `IBoardResolver` by source id out of the keyed DI registrations.

## LegacyWatchlistImporter

One-shot import of the retired `watchlist.json` into PostgreSQL: runs after the migration, only while there is no
watchlist at all, and creates a single watchlist named `default` with the old `defaultFilter` and entries. After that
the file is dead weight - the database is the only source of truth and the bot is the only way to change it.

# Routines

## PollingWorker

Runs `PollingOrchestrator.RunCycleAsync` in a loop. Between cycles it waits on `IPollingTrigger` instead of a plain
delay, so a new watchlist entry starts a cycle immediately; overlapping runs are prevented by the orchestrator gate.

## RegistryPollingWorker

Drives `RegistryPollingService` every `RegistryPolling:CycleIntervalMinutes` after a start delay. Independent from
`PollingWorker`: the watchlist feed keeps its own cadence and is never blocked by the registry sweep.

## OutboxCleanupWorker

Deletes `Delivered` outbox rows older than `Delivery:DeliveredRetentionHours` every
`Delivery:CleanupIntervalMinutes`. Only delivered rows are touched - pending, leased and dead letters stay.

## OutboxDispatcher

### Flow:

- Mark 'pending' outbox letters with exhausted attempts as 'dead'
- Compute the **cutoff** - which letters are allowed to leave (see below)
- Read and lease 'pending' outbox letters enqueued before it
- Send messages to sink
- Mark outbox letter
    - 'sent' on success
    - 'pending' on failure and reschedule retry (telegram response timeout or exponential backoff)

### The cutoff

The loop ticks every `Delivery:DispatchOutboxIntervalSeconds` (5), while a traversal commits **per board**. Leasing
whatever is pending on every tick therefore produced one message per company, each one stamped with the same
`Delivery:GroupChangesWithinMinutes` window header - the grouping `MessageFormatter` does was never handed anything
to group. `CutoffAsync` is the fix: a letter of the window still being filled stays pending.

Three things open the gate, which is why it is not a plain «wait five minutes»:

- the window closed (`DeliveryWindow.Floor`) - the ordinary case, and what makes the messages read as five minute
  ranges;
- **every traversal is idle** (`ITraversalProgressTracker`) - the walk is over, nothing more can land in the open
  window, so holding it back would only delay the report. `CycleFinished` is raised after the last commit of a
  cycle, which is what makes this safe;
- the open window already holds `Delivery:FlushWindowAfterChanges` letters - that is more than one message anyway,
  so there is nothing left to group.

Two cycles can still interleave inside one window - the watchlist cycle ends and flushes, the registry sweep starts
a moment later and fills the same window - and that second report repeats the header. One message per cycle is the
floor here; the per-company flood is what the cutoff removes.

