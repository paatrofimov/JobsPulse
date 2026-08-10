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
- Read and lease 'pending' outbox letters batch
- Send messages to sink
- Mark outbox letter
    - 'sent' on success
    - 'pending' on failure and reschedule retry (telegram response timeout or exponential backoff)

