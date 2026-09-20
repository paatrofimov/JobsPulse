# JobsPulse

A self-hosted vacancy monitoring service. It polls the careers pages of companies you pick and reports every change —
new, updated or closed vacancy — filtered by your own rules. It talks to the ATS behind each careers page
(Greenhouse, Lever, Workday, …) rather than to a job aggregator, so postings arrive as the company publishes them.

The service is a .NET host with background routines and a PostgreSQL database; a Telegram bot is its user interface —
buttons, no commands.

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
 ATS APIs ─────> polling cycle ─────> database ─────> dispatcher ─────> Telegram bot
                                                                            ^
 Common Crawl ─> discovery ─> board registry ─> registry sweep              └── screens, buttons
```

A background cycle walks every board of every enabled watchlist, compares what it fetched against stored state and
produces changes. A board is fetched once per cycle however many watchlists want it, and a vacancy matching no
enabled filter is never stored, which is what keeps the workload bounded while a second, slower sweep walks the
thousands of boards discovery has found.

Delivery goes through a transactional outbox: the state change and the notification it produced are written in one
transaction, so neither can exist without the other, and the dispatcher retries with backoff on its own schedule
without touching the polling cycle. The bot only renders — every screen reads the database and every button writes
to it.

## Tech stack

| Area | Used |
|---|---|
| Language, runtime | C# 13, .NET 9 |
| Data | PostgreSQL 16, EF Core 9, Npgsql |
| Bot | `Telegram.Bot` |
| Integrations | Greenhouse, Lever, SmartRecruiters, Ashby, Workday, SuccessFactors careers APIs; Common Crawl (DuckDB over remote Parquet) |
| Runtime | `Microsoft.Extensions.Hosting` background services, transactional outbox, rate limiting |
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
