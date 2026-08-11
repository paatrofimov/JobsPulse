Workday source: the backend of the public careers site (`https://{host}/wday/cxs/{tenant}/{site}`). This is what the
job board frontend itself calls - no credentials, not the enterprise API.

Differences from the other sources that shape this project:

- **a board is not a slug.** It needs a host (which carries the cluster), a tenant and a site. The tenant is not
  derivable from the host: `wd3.myworkdaysite.com/recruiting/pwc/...` has no company subdomain at all. So Workday is
  the reason `BoardCandidate` / `WatchlistEntry` / `RegisteredBoard` / `BoardWorkItem` / `SourceTarget` carry a
  `Configuration` json, and the board id is the canonical rendering `{host}/{tenant}/{site}` of it.
- **no resolution by name.** A company name predicts neither the cluster nor which site is public, so
  `ResolveByNameAsync` returns nothing and boards are added by url. For the same reason there is no
  `IBoardUrlParser`: a crawled url carries no confirmed tenant, so Workday stays out of the crawl index sweep.
- **the page size is capped at 20** - the list endpoint answers HTTP 400 above that.
- **`total` is only trustworthy on the first page**, and some tenants cap it (NVIDIA reports 2000) and then *wrap
  back to the first page* instead of answering an empty one. Paging therefore also stops on a page that brings no
  new `externalPath`.
- **existence is two status codes.** 404 means the tenant is fine but the site is not there; 422 means Workday does
  not know the tenant. Everything else - 5xx, a timeout, a body that no longer deserializes - is a failure, never a
  missing board.
- **`postedOn` is relative and human** ('Posted 13 Days Ago'), so it is not mapped at all: it would rewrite the
  content hash every day. The only real date is `startDate` on the per-vacancy endpoint.
- **descriptions cost one request each** - the list carries none.

# Models

## WorkdayBoardConfig

The address of a board: `Host`, `Tenant`, `Site`, `Kind`. Serialized as the board configuration json and the single
place that builds urls from it - `BoardUrl` and `JobUrl` point at the careers site, `CxsBaseUrl` and `CxsJobUrl` at
the backend. `BoardId` is the derived identity `{host}/{tenant}/{site}`; `FromBoardId` parses it back, which is the
fallback for a row written before configurations existed and for a board added by hand with `/board_add`.

## WorkdayHostKind

Which of the two public host schemes serves the site: `MyWorkdayJobs` (`{sub}.{cluster}.myworkdayjobs.com/{site}`) or
`MyWorkdaySite` (`{cluster}.myworkdaysite.com/recruiting/{tenant}/{site}`). Kept in the configuration so url building
never has to sniff the host again.

## WorkdayUrlParts

What a url tells us on its own: host, site, host kind, whether it addressed the board or a single vacancy, and a
*tenant hint* - never a tenant. See `WorkdayBoardResolver` for how a hint is confirmed.

## WorkdaySitePair

The tenant and site as the careers page itself reports them.

## JobsDtos

`JobsPageDto` / `JobPostingDto` for the list, `JobDetailDto` / `JobPostingInfoDto` for one vacancy. Every field is
nullable: this is an unversioned frontend contract, and a field that disappears must cost one field, not the board.
`ExternalPath` is the only one a posting cannot be mapped without - postings missing it are dropped and counted.

## WorkdayFetch

`Ok` / `Missing` / `Failure`. `Missing` is reserved for a board that really is not there, so a broken contract can
never be reported as a board that stopped existing.

# Infrastructure

## WorkdayBoardUrl

Reads a board out of any public Workday url and collapses every form of one board onto one address: the board url,
its locale-prefixed form (`/en-US/External`) and a deep link to a single vacancy all normalize to the same
`WorkdayUrlParts`. Returns null for anything that is not a Workday host - every resolver is asked about every url.

## WorkdayCareersSiteClient

Confirms the tenant. The careers page bootstraps its frontend with `window.workday = { tenant, siteId }` - the same
pair the frontend then calls the backend with, which is what makes the tenant confirmable instead of guessed. A
missing site answers 404; an unknown tenant answers 500 and so does a real outage, which is why only the 404 is
reported as missing and the rest is an unconfirmed answer.

## WorkdayCxsClient

`GetJobsAsync` is a POST with `{ appliedFacets, limit, offset, searchText }`; `GetJobAsync` is the per-vacancy
endpoint (the board base plus the posting's `externalPath`). `MaxPageSize` is 20 because the endpoint rejects more.
404 and 422 become `Missing`, 429 a failure with the retry hint, and a `JsonException` a failure carrying
`contract error` - deliberately not a missing board.

## WorkdayPostingIdentity

Identity out of `externalPath`. The last path segment ends with the requisition token
(`.../GPU-Verification-Engineer_JR2015943`), so `PostId` is everything after the final `_`; a path without a token
falls back to the path itself. The title is never part of the identity - it is the rest of that same segment, and a
retitled vacancy must read as an update, not as one vacancy closing and another opening.

`GroupId` drops Workday's repost suffix (`JR2022750-1` → `JR2022750`), so several postings of one requisition group.
The suffix is matched greedily and limited to two digits on purpose: `JR-119418` is one Sony requisition id, not
requisition `JR` reposted 119418 times - reading it the other way would give a whole board one `GroupId` and let
`ChangeDetector` collapse unrelated jobs that share a location.

## WorkdayMapper

`JobPostingDto` (plus an optional detail) to `Vacancy`. `Url` is the detail's `externalUrl` when there is one and
otherwise built from the configuration - always the careers site, never the backend. `locationsText` is sometimes a
count rather than a place ('2 Locations'), so it is dropped in that case and the location comes from the detail or
stays null. `Offices` is the detail's location plus its `additionalLocations`, falling back to the single listed
place. `FirstPublishedAt` is the detail's `startDate`; `UpdatedAt` stays null, so change detection relies on the
content hash alone.

## WorkdayBoardSource

The configuration comes from `SourceTarget.Configuration`, falling back to parsing the board id. An unreadable one is
a failure, not a missing board - the board may well exist, we just cannot address it.

Paging stops on the first of: an empty page, a page that brings no new `externalPath` (the wrap guard), a short page,
the reported total being reached, or `MaxPages`. Only the last of those is an incomplete traversal: reaching a total
that is really the tenant's own cap still counts as complete, because treating it otherwise would mean never
committing state for a large board.

Descriptions are then fetched per vacancy within `MaxDescriptionRequests`, exactly as SmartRecruiters does it;
postings past the budget are mapped from the list alone and the count is logged.

## WorkdayBoardResolver

`ResolveByUrlAsync` is the way a board is added: normalize the url, confirm tenant and site against the careers page,
then probe the backend. When the page cannot confirm (500 - unknown tenant, or an outage), the hint from the url is
tried and the backend adjudicates; that candidate is reported as `Guessed` rather than `DirectSlug`. Every candidate
carries the serialized configuration, so the entry written to the watchlist is addressable.

`ProbeAsync` takes the canonical board id, and is also the validation step for a manual `/board_add`. `JobCount` is
the reported total, `DisplayName` the tenant - what the company is called inside Workday.
