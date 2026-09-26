# JobsPulse

A self-hosted vacancy monitoring service. It polls the careers pages of companies you pick and reports every change —
new, updated or closed vacancy — filtered by your own rules. It talks to the ATS behind each careers page
(Greenhouse, Lever, Workday, …) rather than to a job aggregator, so postings arrive as the company publishes them.

The state lives in PostgreSQL, the routines run as one-shot GitHub Actions jobs, and the user interface is a Telegram
bot — buttons, no commands — served as a webhook on Google Cloud Run.

---

## Getting started

1. Open the bot in Telegram — [@JobsPulseBot](https://t.me/JobsPulseBot), or your own instance — and press **/start**.
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

## Running it yourself

```bash
# PostgreSQL
docker run -d --name jobspulse-pg -e POSTGRES_PASSWORD=postgres -p 5432:5432 postgres:16

# A bot token from @BotFather
cd src/JobsPulse.Host
dotnet user-secrets set "Telegram:BotToken" "<token>"
dotnet user-secrets set "ConnectionStrings:Postgres" \
  "Host=localhost;Database=jobspulse;Username=postgres;Password=postgres"

# Migrations are applied on start
dotnet run
```

`dotnet run` runs every routine and the bot (long polling) in one process.

## Deployment

Every workflow in `.github/workflows` builds the host and runs it with a role. The routines are started by
[cron-job.org](https://cron-job.org) via `workflow_dispatch`, because GitHub does not fire their own `schedule:` in
this repository.

| Workflow | Started | What it does |
|---|---|---|
| `polling` | hourly, :05 UTC | Polls every board of every enabled watchlist, detects new / updated / closed vacancies and sends them to Telegram while the cycle runs. |
| `registry` | :17 and :47 UTC | Background sweep of the discovered board registry: the 50 least recently polled boards per run; a board whose vacancies match a watchlist filter is added to it (🔎, up to 5 per run). |
| `discovery` | daily, 03:23 UTC | Mines Common Crawl indexes for ATS board urls to fill the registry. Stops after 330 minutes and continues from its checkpoint on the next run. |
| `cleanup` | daily, 04:53 UTC | Deletes delivered notifications older than 24 hours from the outbox. |
| `deploy-bot` | on push to `master` | Ships the Telegram bot: builds the Docker image, deploys it to Cloud Run as `--role webhook` (scales to zero, one instance) and registers the webhook and the command menu. Skipped until `GCP_PROJECT_ID` is set. |

`_run-job.yml` is the shared build-and-run step of the routines; `schedule-probe` is a temporary check of GitHub's own
`schedule:`. `--role bot` (long polling) remains for a long-living host and steps aside while a webhook is registered.

Google Cloud, once: a project with billing, the **Cloud Run Admin API** and **Artifact Registry API** enabled, and a
service account with **Cloud Run Admin**, **Artifact Registry Administrator** and **Service Account User** roles and a
JSON key.

Repository **Settings → Secrets and variables → Actions**:

| Name | Kind | Used by | Value |
|---|---|---|---|
| `POSTGRES` | secret | routines, bot | connection string of the database |
| `TELEGRAM_BOT_TOKEN` | secret | routines, bot | token from @BotFather |
| `GCP_PROJECT_ID` | variable | `deploy-bot` | Google Cloud project id |
| `GCP_REGION` | variable, optional | `deploy-bot` | Cloud Run region, default `europe-west3` (next to a Frankfurt database) |
| `GCP_SA_KEY` | secret | `deploy-bot` | JSON key of the deploy service account |
| `TELEGRAM_WEBHOOK_SECRET` | secret | `deploy-bot` | random string (letters, digits, `_`, `-`) Telegram sends with every request |
| `GH_DISPATCH_TOKEN` | secret | bot | fine-grained token, **Actions: read and write** — lets the bot start `polling` |
| `POLLING_DRY_RUN` | variable, optional | `polling` | `true` — poll without enqueueing notifications |

cron-job.org needs its own fine-grained token with **Actions: read and write**
(`POST .../actions/workflows/<workflow>.yml/dispatches`, body `{"ref":"master"}`).
