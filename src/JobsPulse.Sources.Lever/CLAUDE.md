Lever source: postings API (`https://api.lever.co/v0/postings/{site}?mode=json`) on both Lever instances - the
global one and the EU one (`api.eu.lever.co`). They differ by host only, so one client serves both.

Differences from Greenhouse that shape this project:

- a site lives on exactly one instance, and nothing in the board id says which one
- the API pages (`skip` / `limit`, 100 max) and reports no total, so «the last page» is a short page
- it accepts server-side filters (`location`, `team`, `department`, `commitment`, `level`), so the board can be
  narrowed before download
- there is no job-level id: one job in many locations is one posting with `categories.allLocations`
- an unknown site answers `200 []`, not 404

# Infrastructure

## LeverRegion

The two instances as data: api base url and public job board host, plus `Global`, `Eu` and `All`. Everything
instance-specific lives here, which is what keeps the rest of the project single-path.

## LeverRegionMap

Singleton cache «site to instance», filled by the first successful probe and logged once per site. In-memory on
purpose: the mapping is derived, it is cheap to re-probe once per process, and it does not deserve a table.

## LeverPostingsClient

Thin client: one request is one page, built as an absolute url from the instance of the site - the named HttpClient has
no base address. Filters from `LeverOptions` are appended as repeated query keys (OR-ed by the API). 404 is a missing
board, 429 is reported as a failure with the retry hint.

### Instance lookup

The instance of an unknown site is probed with one unfiltered posting per instance, in `Regions` order, and then
remembered - paging and later cycles cost no extra request. Because an unknown site answers `200 []`, emptiness is the
«not on this instance» signal.

Two failure modes are kept apart on purpose: a site that no instance knows answers as an empty board (today's
behaviour - its vacancies are closed), while an instance that could not be asked at all is reported as a failure, so a
temporary outage never closes stored vacancies.

## LeverBoardSource

Resolves the instance once (cached) and pages with `skip`/`limit` until a short page arrives. `MaxPages` is a safety cap - hitting it returns an
incomplete traversal, so the orchestrator drops the batch instead of closing everything it did not fetch.

## LeverBoardResolver

Name resolution reuses `CompanySlugGuesser`; url resolution takes the site out of a `jobs.lever.co` or `jobs.eu.lever.co`
link (one pattern for both) or scans a career page for one. `ProbeAsync` requests one unfiltered page - a site that answers with an empty array is
treated as non-existent, otherwise discovery would store every random path segment as a board. `BoardUrl` is built from
the instance the probe resolved, so a global site is never linked to an EU host.

## LeverBoardUrlParser

`IBoardUrlParser` for crawl index mining: `jobs.lever.co/*`, `jobs.eu.lever.co/*`, `api.lever.co/v0/postings/*`,
`api.eu.lever.co/v0/postings/*` - the crawl index knows nothing about instances, so both are mined.

## LeverSiteSlug

Site extraction shared by the resolver and the url parser; reserved segments (`v0`, `postings`, `apply`, ...) are
rejected.

## LeverMapper

`PostingDto` to `Vacancy`. The url falls back to the job board host of the site's instance when the posting carries
neither `hostedUrl` nor `applyUrl`. `createdAt` is unix milliseconds and becomes `FirstPublishedAt`; there is no update
timestamp, so `UpdatedAt` stays null and change detection relies on the content hash alone.
