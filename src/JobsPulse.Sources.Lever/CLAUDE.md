Lever eu source: postings API (`https://api.eu.lever.co/v0/postings/{site}?mode=json`).

Differences from Greenhouse that shape this project:

- the API pages (`skip` / `limit`, 100 max) and reports no total, so «the last page» is a short page
- it accepts server-side filters (`location`, `team`, `department`, `commitment`, `level`), so the board can be
  narrowed before download
- there is no job-level id: one job in many locations is one posting with `categories.allLocations`
- an unknown site answers `200 []`, not 404

# Infrastructure

## LeverPostingsClient

Thin client: one request is one page. Filters from `LeverOptions` are appended as repeated query keys (OR-ed by
the API). 404 is a missing board, 429 is reported as a failure with the retry hint.

## LeverBoardSource

Pages with `skip`/`limit` until a short page arrives. `MaxPages` is a safety cap - hitting it returns an
incomplete traversal, so the orchestrator drops the batch instead of closing everything it did not fetch.

## LeverBoardResolver

Name resolution reuses `CompanySlugGuesser`; url resolution takes the site out of a `jobs.lever.co` link or scans
a career page for one. `ProbeAsync` requests one unfiltered page - a site that answers with an empty array is
treated as non-existent, otherwise discovery would store every random path segment as a board.

## LeverBoardUrlParser

`IBoardUrlParser` for crawl index mining: `jobs.lever.co/*`, `jobs.eu.lever.co/*`,
`api.eu.lever.co/v0/postings/*`.

## LeverSiteSlug

Site extraction shared by the resolver and the url parser; reserved segments (`v0`, `postings`, `apply`, ...) are
rejected.

## LeverMapper

`PostingDto` to `Vacancy`. `createdAt` is unix milliseconds and becomes `FirstPublishedAt`; there is no update
timestamp, so `UpdatedAt` stays null and change detection relies on the content hash alone.
