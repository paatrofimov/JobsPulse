# Routines

## PollingWorker

Runs `PollingOrchestrator.RunCycleAsync` in a loop. Between cycles it waits on `IPollingTrigger` instead of a plain
delay, so a new watchlist entry starts a cycle immediately; overlapping runs are prevented by the orchestrator gate.

## OutboxDispatcher

### Flow:

- Mark 'pending' outbox letters with exhausted attempts as 'dead'
- Read and lease 'pending' outbox letters batch
- Send messages to sink
- Mark outbox letter
    - 'sent' on success
    - 'pending' on failure and reschedule retry (telegram response timeout or exponential backoff)

