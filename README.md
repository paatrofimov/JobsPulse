# JobsPulse
~~~~
**A Telegram bot that watches the careers pages of the companies you care about and messages you the moment a matching
vacancy appears.**

No job board, no aggregator feed, no daily digest of noise: you name the companies, you say what a good vacancy looks
like, and you get a message per change — new, updated, closed.

---~~~~

## 1. Use it

1. Open the bot in Telegram: **[@JobsPulseBot](https://t.me/JobsPulseBot)** — *replace with your own bot handle if you
   self-host; see [Run it](#4-run-it-yourself).*
2. Press **/start**. Everything after that is buttons — there are no commands to memorize.
3. **📋 My watchlists → ➕ New watchlist** — a watchlist is a named set of companies plus one filter.
4. **➕ Add company** — type a company name or paste a link to its careers page. The bot finds the board
   itself; you never see an ATS name or a board id.
5. **🔧 Filter** — words that must be in the title, words that must not, locations, how fresh a vacancy may be.
6. Done. New matches arrive as messages, grouped by company. **💼 Vacancies** shows everything currently open at any
   time.

### What problems does this solve

| Problem | What the bot does |
|---|---|
| Company careers pages have no alerts, or the alerts are spam | Polls the ATS directly, every 10 minutes by default |
| You track 40 companies in a spreadsheet and re-check them by hand | One watchlist, checked for you, changes pushed |
| Aggregators show stale postings and reposts | Reports *changes*: new, updated, closed — deduplicated by content |
| You forget which companies you already applied to | Mark a company **✅ worked through**; the date is kept |
| A vacancy appears and is gone in two days | You hear about it within minutes, with a 🔥 mark if it is fresh |
| You want to find companies you did not know about | Discovery mines Common Crawl for job boards and adds the ones matching your filter |

Interface and notifications are available in **English and Russian**, switchable per user.

---

## 2. How it works

```
 Source APIs                 Core pipeline                     PostgreSQL                Telegram
 ───────────                 ─────────────                     ──────────                ────────
 Greenhouse  ┐                                            ┌── seen_vacancy       (global board state)
 Lever       │   PollingWorker → PollingOrchestrator  ────>┤   watchlist_vacancy  (match layer)
 SmartRecr.  │      └─ BoardProcessor                      └── outbox ──> OutboxDispatcher ──> TelegramSink
 Ashby       ├──>      └─ ChangeDetector (pure)                                                   │
 Workday     │                                                                                    v
 SuccessF.   │                                                                              BotUpdateHandler
 HeadHunter  ┘                                                                               (screens, buttons)

 Common Crawl ──> Discovery ──> board_registry ──> RegistryPollingService
                                                    └─ DiscoveredBoardPromoter
```

**The watchlist is the processing boundary.** A watchlist is a named set of boards plus one filter, owned by a bot user.
State is deliberately split in two levels, because one board may belong to several watchlists:

* `seen_vacancy` — the global state of an ATS post (source/board/post). Soft-closed, never deleted, change detected by a
  content hash rather than the source's own `updated_at` (ATS boards bump it on cosmetic edits).
* `watchlist_vacancy` — the match layer: *this post passed the filter of this watchlist, and this content was already
  reported to it*. This is what lets one vacancy be new in one watchlist, closed in another, and produce exactly one
  notification per watchlist.
]
**A board is fetched once per cycle** no matter how many watchlists want it (`WatchlistPlan` collapses them into
`BoardWorkItem`s). Vacancies matching no enabled filter are not stored at all, which is what keeps the table bounded
while the registry sweep walks thousands of boards.

**Delivery is a transactional outbox.** `ChangeDetector` is a pure function; `StateStore.CommitAsync` writes vacancy
upserts, closures, match rows and outbox notifications in one transaction, so a notification can never exist without the
state that produced it. `dedup_key` is unique in the database, so a retried cycle cannot send twice.
`OutboxDispatcher` leases, sends, retries with backoff and dead-letters.

**Discovery** mines Common Crawl indexes (Parquet via DuckDB, HTTP index as fallback) for ATS URLs, validates each token
against the ATS, and accumulates `board_registry`. A secondary sweep polls registry boards and *promotes* a board into a
watchlist when it matches that watchlist's filter — insert-only, so a board you dropped is never resurrected.

**The bot is the whole UI.** One screen per class, each returning a `ScreenView` (HTML + inline keyboard); a button press
edits the message in place instead of appending to the chat. `WatchlistAccess` is the single ownership chokepoint —
your list is editable, somebody else's is a read-only example. Everything raw (board ids, filter JSON, the registry,
`/force_cycle`) lives behind the admin section, gated on `Telegram:AdminUsernames` — which also opens with the
traversal progress: how many boards of each source have been walked, and how much of the crawl index dataset is mined
(`/progress`).

### Projects

| Project | Responsibility |
|---|---|
| `JobsPulse.Core` | Domain model, watchlists, polling orchestration, change detection, filtering, abstractions |
| `JobsPulse.Storage` | PostgreSQL persistence: EF Core reads, raw Npgsql upserts, migrations |
| `JobsPulse.Sources.*` | One project per source: Greenhouse, Lever (global + EU), SmartRecruiters, Ashby, Workday, SuccessFactors, plus HeadHunter — a centralized employer catalog rather than an ATS |
| `JobsPulse.Discovery` | Common Crawl mining and board validation |
| `JobsPulse.Sinks.Telegram` | The bot: screens, localization, formatting, delivery |
| `JobsPulse.Host` | Composition root and the background workers |

Each project carries a `CLAUDE.md` documenting its modules and, more importantly, *why* they look the way they do.

---

## 3. Tech stack

**Language & runtime:** C# 13, .NET 9.

**Data:** PostgreSQL 16 · EF Core 9 (reads, migrations) · Npgsql.

**Integrations:** Telegram Bot API (`Telegram.Bot`) · Greenhouse, Lever, SmartRecruiters, Ashby, Workday and
SuccessFactors careers APIs · HeadHunter API (employer catalog; its search endpoints need an application token since
April 2026)
· Common Crawl (DuckDB over remote Parquet, HTTP index fallback).

**Runtime & patterns:** `Microsoft.Extensions.Hosting` background services · options pattern with hot reload
(`IOptionsMonitor`) · transactional outbox with leasing, exponential backoff and dead-lettering · `SemaphoreSlim` gating and per-board timeouts · rate limiting
(`System.Threading.RateLimiting`) · structured logging (Vostok, console + file).

**Testing:** NUnit · FluentAssertions · FakeItEasy.

---

## 4. Run it

```bash
# 1. PostgreSQL
docker run -d --name jobspulse-pg -e POSTGRES_PASSWORD=postgres -p 5432:5432 postgres:16

# 2. Secrets (a bot token from @BotFather)
cd src/JobsPulse.Host
dotnet user-secrets set "Telegram:BotToken" "<token>"
dotnet user-secrets set "ConnectionStrings:Postgres" "Host=localhost;Database=jobspulse;Username=postgres;Password=postgres"

# 3. Run — migrations are applied on start
dotnet run
```

In production, configuration comes from environment variables (`Telegram__BotToken`).
`src/JobsPulse.Host/appsettings.json` holds infrastructure settings only — polling cadence, delivery caps, discovery,
per-ATS options and `Telegram:AdminUsernames`. What is watched lives in the database and is changed only through the
bot.

```bash
dotnet build JobsPulse.sln
dotnet test                     # integration tests hit real ATS endpoints and a real database
```
