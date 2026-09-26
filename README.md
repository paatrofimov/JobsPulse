# JobsPulse

A vacancy monitoring service. It polls the careers pages of companies you pick and reports every change —
new, updated or closed vacancy — filtered by your own rules. It talks to the ATS behind each careers page
(Greenhouse, Lever, Workday, …) rather than to a job aggregator, so postings arrive as the company publishes them.

The state lives in PostgreSQL, the routines run as one-shot GitHub Actions jobs, and the user interface is a Telegram
bot — buttons, no commands — served as a webhook on Google Cloud Run.

---

## Getting started

1. Open [@JobsPulseBot](https://t.me/JobsPulseBot) in Telegram and press **/start**.
2. **📋 My watchlists → ➕ New watchlist.** A watchlist is a named set of companies plus one filter.
3. **➕ Add company** — type a company name or paste a link to its careers page. The service resolves the board
   itself; you never see an ATS name or a board id.
4. **🔧 Filter** — words wanted and unwanted in the title, in the location and in the description, plus how fresh a
   vacancy may be.
5. Matching changes now arrive as messages. **💼 Vacancies** shows what is currently open at any time.

Interface and notifications are available in English and Russian, switchable per user.

## Features

- **Watchlists.** A named set of companies and one filter, per user. Several watchlists are independent; one company
  may sit in many of them.
- **Change detection, not a feed.** Each posting is tracked by a content hash, so an ATS bumping its own `updated_at`
  on a cosmetic edit produces nothing. Closed vacancies are reported too.
- **Batched notifications.** Everything found within a 5-minute window arrives as one message, with a collapsible
  block per company instead of one message per change.
- **Browsable lists.** Vacancies and companies can be grouped by company, by region, by month, or by company activity.
- **Company activity indicator.** Vacancy events per month on a board (opened / changed / closed) — a rough measure
  of where hiring is actually moving, and a quick way to spot a source that misreports changes.
- **Discovery.** Common Crawl indexes are mined for ATS URLs to build a registry of boards that exist; a board whose
  vacancies match a watchlist filter is proposed into that watchlist automatically.
- **Sources.** Greenhouse, Lever (global and EU), SmartRecruiters, Ashby, Workday, SuccessFactors.

## Architecture

```
 cron-job.org ─ workflow_dispatch ─> GitHub Actions, one-shot jobs
                                       polling    watchlist boards (ATS APIs) ─┐
                                       registry   registry boards (ATS APIs) ──┼─> PostgreSQL ─> outbox ─> Telegram
                                       discovery  Common Crawl ─> registry ────┘
                                       cleanup    delivered outbox rows

 Telegram ─ webhook ─> bot on Cloud Run ─> PostgreSQL      (adding a company starts `polling`)
```

Every routine is a job that runs one iteration and exits. The polling cycle walks every board of every enabled
watchlist, compares what it fetched against stored state and produces changes. A board is fetched once per cycle
however many watchlists want it, a vacancy matching no enabled filter is never stored, and per-vacancy detail requests
are made only for new or changed postings — which keeps the workload bounded while the registry sweep walks the
thousands of boards discovery has found.

Delivery goes through a transactional outbox: the state change and the notification it produced are written in one
transaction, so neither can exist without the other. The job that walks the boards also sends the notifications, in
5-minute windows while the cycle runs, drains the rest at the end and retries with backoff.

The bot only renders — every screen reads the database and every button writes to it. Telegram sends each message and
button tap to the Cloud Run service, which starts the container on demand and scales back to zero when nobody uses it.

## Tech stack

| Area | Used |
|---|---|
| Language, runtime | C# 13, .NET 9 |
| Data | PostgreSQL (Neon in production), EF Core 9, Npgsql |
| Bot | `Telegram.Bot`; webhook on ASP.NET Core minimal API |
| Integrations | Greenhouse, Lever, SmartRecruiters, Ashby, Workday, SuccessFactors careers APIs; Common Crawl (DuckDB over remote Parquet) |
| Routines | `Microsoft.Extensions.Hosting`, transactional outbox, bounded concurrency |
| Hosting | GitHub Actions (routines, started by cron-job.org), Google Cloud Run (bot, Docker image in Artifact Registry) |
| Logging | Vostok, console and file |
| Testing | NUnit, FluentAssertions, FakeItEasy |

## Deployment

Nothing is always on — every part runs on a free tier and wakes up only when there is work:

| Part | Where | How it runs |
|---|---|---|
| Routines | GitHub Actions | one-shot jobs, started on a schedule by cron-job.org |
| Telegram bot | Google Cloud Run | webhook container, started by incoming messages, scales to zero |
| Database | Neon | managed PostgreSQL |

| Job | Runs | What it does |
|---|---|---|
| `polling` | hourly | Polls every board of every enabled watchlist, detects new / updated / closed vacancies and sends them to Telegram while the cycle runs. |
| `registry` | twice an hour | Sweeps the discovered board registry, least recently polled boards first; a board whose vacancies match a watchlist filter is added to it (🔎). |
| `discovery` | daily | Mines Common Crawl indexes for ATS board urls to fill the registry, continuing from its checkpoint on every run. |
| `cleanup` | daily | Deletes delivered notifications from the outbox. |

Every push to `master` rebuilds the bot image and redeploys it to Cloud Run.
