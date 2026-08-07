# Routines

## OutboxDispatcher

### Flow:

- Mark 'pending' outbox letters with exhausted attempts as 'dead'
- Read and lease 'pending' outbox letters batch
- Send messages to sink
- Mark outbox letter
    - 'sent' on success
    - 'pending' on failure and reschedule retry (telegram response timeout or exponential backoff)

