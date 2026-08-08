# Abstractions

## IVacancySink

Sink implementations must implement formatting and sending.

## IBoardResolver

Searching board via human-readable name - bot command /watch {company_name}

# PersistentModels

Models that are stored in the database and must be used only in the storage layer.

## PersistentOutboxStatus

- Intermediate
    - Pending: ready for delivery if next attempt is due; can switch to Lease status
    - Lease: delivery is in progress; can switch to Delivered status on success OR Pending status on error with rescheduling
- Terminal
    - Delivered: message was delivered successfully
    - Dead: delivery attempts are exhausted