Ashby source: public posting API (`https://api.ashbyhq.com/posting-api/job-board/{board}`). The board id is the job
board name from `jobs.ashbyhq.com/{board}`; lookup is case-insensitive, so it is stored lowercase and one board is
always one registry row.

Differences from the other sources that shape this project:

- the whole board arrives in one unauthenticated response: no paging, no filtering, no per-job request, and the
  descriptions are already there (`descriptionPlain` / `descriptionHtml`)
- an unknown board answers 404, so «does not exist» is unambiguous - unlike Lever and SmartRecruiters
- `isListed: false` marks a posting that exists but must not be published, so it is dropped by default
- one job is one posting: extra locations live inside it as `secondaryLocations`, so there is nothing to group by
- there is no update timestamp, only `publishedAt`

# Infrastructure

## AshbyJobBoardClient

Thin client: one request is the whole board. 404 is a missing board, 429 is reported as a failure with the retry hint.
The client takes no options - the endpoint accepts nothing but the board name.

## AshbyBoardSource

One request, one traversal: a successful answer is always complete, so the orchestrator never has to drop a partial
batch. Unlisted postings are filtered out unless `IncludeUnlisted` is set, and the skipped count is logged.

## AshbyBoardResolver

Name resolution reuses `CompanySlugGuesser`; url resolution takes the board out of an `ashbyhq.com` link or scans a
career page for one. `ProbeAsync` reads the board once and reports the number of listed postings as `JobCount`; a
board without postings is treated as non-existent, otherwise discovery would store every random path segment.

## AshbyBoardUrlParser

`IBoardUrlParser` for crawl index mining: `jobs.ashbyhq.com/*`, `api.ashbyhq.com/posting-api/job-board/*`.

## AshbyJobBoardSlug

Board extraction shared by the resolver and the url parser; reserved segments (`posting-api`, `job-board`, `embed`,
...) are rejected.

## AshbyMapper

`JobDto` to `Vacancy`. `publishedAt` becomes `FirstPublishedAt`; `UpdatedAt` stays null, so change detection relies on
the content hash alone. `GroupId` is null - one posting per job. `Location` is the primary location, marked
`(remote)` when `isRemote`, and `Offices` is the primary location plus the secondary ones.
