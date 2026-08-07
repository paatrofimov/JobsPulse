# Pipeline

## PollingOrchestrator

### Flow:

- read watchlist entries
- identify source
- traverse board
    - if board is missing, disable entry in watchlist
- filter matching board vacations
- load seen vacancies from the same source and board
- detect vacancies changes from stored seen vacancies
- if board is traversed for the first time
    - then mark watchlist entries as seeded
    - else build outbox notifications
- update state as a single transaction:
    - upsert new seen vacancy
    - insert closed vacancies
    - enqueue outbox

### ChangeDetector

Analyzes traversal result after filter and produces new, changed and closed vacancies.

### VacancyHasher

Calculates vacancy hash which is used for deduplicating and tracking vacancy changes.

### VacancyMatcher

Applies filter to a list of vacancies.

### WatchService

Resolves company by name.

### Flow:
- already in wathclist
- if passed url instead of name then try parse career page
- if resolved by name then show board candidates
- nothing found

# Abstractions

## IVacancySink

Sink implementations must implement formatting and sending.

## IBoardResolver

Searching board via human-readable name - bot command /watch {company_name}

## IStateStore

Responsible for atomic updates of seen vacancies and enqueueing outbox notifications.

# Model Infrastructure

## FilterSpec

Filter specification.

### PostedWithinDays

Truncate old vacancies (null - no truncation).

### MatchMode

Case-insensitive

- Substring: substring, default
- Exact
- Regex: NonBacktracking, timeout — a bad pattern should not hang the worker

## BoardCandidate

Showed to user on search by name.

# Model Domain

## Vacancy

Normalized vacancy - common for all ATS.

### SourceId

The id of a source: greenhouse, lever, etc.

### BoardId

The id of a board inside a source, for example, 'board_token' for greenhouse.

### PostId

The id of a post inside a board, for example 'id' for greenhouse. Unique within a board.

### Key

Format: {SourceId}/{BoardId}/{PostId}

### GroupId

The id of a job itself. Single job can be repeated across many posts (different locations, languages, etc.). null for prospect posts - posts listing that is not tied to a specific job. For example '
internal_job_id' for greenhouse.

### ContentHash

UpdatedAt can be changed on any cosmetic changes. So hash is calculated on each db upsert by important fields instead.

## OutboxItem

### Id

Incremental LONG id.

### DedupKey

- Idempotency key. Single change won't be enqueued twice.
- Format: {Vacancy.Key}|{ChangeKind}|{ContentHash}
