SmartRecruiters source: public posting API (`https://api.smartrecruiters.com/v1/companies/{company}/postings`).
The board id is the company identifier (`jobs.smartrecruiters.com/{company}`); lookup is case-insensitive, so it is
stored lowercase and one company is always one registry row.

Differences from Greenhouse and Lever that shape this project:

- lists are wrapped into `{ offset, limit, totalFound, content }`, so paging is `offset`/`limit` (100 max) against a
  known total - unlike Lever, «the last page» does not have to be guessed
- the list carries no description and no job id: both live in `GET /postings/{id}`, one request per posting
- there is no update timestamp, only `releasedDate`
- an unknown company answers `200` with `totalFound: 0`, not 404
- server-side filters (`q`, `country`, `region`, `city`, `department`, `language`) take a single value each

# Infrastructure

## SmartRecruitersPostingsClient

Thin client: one request is one page or one posting detail. Filters from `SmartRecruitersOptions` are appended when
`applyFilters` is set. 404 is a missing board, 429 is reported as a failure with the retry hint.

## SmartRecruitersBoardSource

Pages with `offset`/`limit` until `totalFound` is covered or a short page arrives. `MaxPages` is a safety cap -
hitting it returns an incomplete traversal, so the orchestrator drops the batch instead of closing everything it did
not fetch.

Descriptions (and with them `GroupId`) are fetched per posting only when the target or `IncludeContentOnPoll` asks
for them, and never more than `MaxDescriptionRequests` times per traversal: the rest of the board is mapped without
a description and the shortfall is logged. A failed detail request is not fatal - the posting is still mapped.

## SmartRecruitersBoardResolver

Name resolution reuses `CompanySlugGuesser`; url resolution takes the company out of a `smartrecruiters.com` link or
scans a career page for one. `ProbeAsync` requests one posting unfiltered and trusts `totalFound` - a company that
answers with zero postings is treated as non-existent, otherwise discovery would store every random path segment as
a board. `JobCount` is `totalFound`, so a probe never pages.

## SmartRecruitersBoardUrlParser

`IBoardUrlParser` for crawl index mining: `jobs.smartrecruiters.com/*`, `careers.smartrecruiters.com/*`,
`api.smartrecruiters.com/v1/companies/*`.

## SmartRecruitersCompanySlug

Company extraction shared by the resolver and the url parser; only the `jobs`, `careers` and `api` hosts carry a
company in the path (`www.smartrecruiters.com` is a marketing site), and reserved segments (`v1`, `companies`,
`postings`, `oneclick-ui`, ...) are rejected.

## SmartRecruitersMapper

`PostingDto` to `Vacancy`. `releasedDate` becomes `FirstPublishedAt`; `UpdatedAt` stays null, so change detection
relies on the content hash alone. `GroupId` is the detail's `jobId` and is null without a detail request, which
means postings pass deduplication through. `Location` prefers `fullLocation` and falls back to city/region/country,
marked `(remote)` or `(hybrid)`; the job ad sections are concatenated into one description.
