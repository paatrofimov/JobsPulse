## PersistentModels

Models that are stored in the database and must be used only in the storage layer.

- PersistentOutboxItem - messages in the outbox

## OutboxStorage

Statuses:

- Intermediate
    - Pending: ready for delivery if next attempt is due; can switch to Lease status
    - Lease: delivery is in progress; can switch to Delivered status on success OR Pending status on error with rescheduling
- Terminal
    - Delivered: message was delivered successfully
    - Dead: delivery attempts are exhausted