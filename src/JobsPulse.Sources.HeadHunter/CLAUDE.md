HeadHunter source: the HeadHunter api (`https://api.hh.ru`) - the same one the platform's own site is built on.

**It is no longer anonymous.** Since April 2026 the search endpoints (`/vacancies`, `/employers`, an employer, a vacancy)
answer HTTP 403 `forbidden` without a token; only the dictionaries (`/areas`, `/dictionaries`, `/suggests`) stayed public.
So this source needs a token of an application registered at `dev.hh.ru` - see «Authorization» - and without one every
board it owns reports `Refused` rather than failing quietly.

This is the first source that is not an ATS, and that is what shapes the project. Every other source is one company's
own job board addressed by something the company owns - a slug, a careers host, a tenant. HeadHunter is a **centralized
catalog of employers**: one api, one namespace, and every company inside it addressed by a numeric `employer_id`. So the
analogue of a board token is that id, and the analogue of «guess the slug» is «search the catalog».

Differences from the ATS sources that follow from it:

- **the board id is an employer id** (`1740`), handed out by the catalog rather than derived from anything. It carries
  the whole address, so `Configuration` stays null - unlike Workday, nothing else is needed to poll it.
- **discovery is a search, polling never is.** `ResolveByNameAsync` asks the employer search for a company name; from
  the moment the id is stored, `HeadHunterBoardSource` addresses the employer directly and no company name is ever
  searched for again. A board id that is not numeric is refused before it costs a request, so a bad row can never
  silently degrade into a search.
- **an exact name cannot be the matching rule.** The catalog spells companies out in full (`ООО «Яндекс.Такси»`), one
  company is usually several employer records (the group, its regions, its brands), and what a user types is a brand.
  The search is fuzzy in return - it is what matches `Yandex` to `Яндекс` - and pays for it with a tail of unrelated
  results. Hence a score and an ordering rather than a comparison: `HeadHunterEmployerMatcher`.
- **there is no per-employer list endpoint.** Vacancies come from the common vacancy search filtered by `employer_id`,
  which is the supported way to ask for a company's board.
- **paging has a ceiling, not just a cap.** The search refuses `page` * `per_page` past ~2000 items, so a large employer
  is continued in publication-date windows - see `HeadHunterBoardSource`.
- **no requisition id**, so nothing can be grouped: one job advertised in three cities is three unrelated vacancy ids.
- **no documented rate limit.** There is no number to stay under, only the api's reaction, which is why the client paces
  itself adaptively instead of assuming an RPS - see `HeadHunterApiClient`.
- **the user agent is part of the contract**: a request the api does not like the agent of is answered HTTP 400
  `bad_user_agent`, whatever else was right about it - and the agent of its own documentation examples is on the
  blacklist, so it cannot be copied either. See `HeadHunterUserAgent`.

# Authorization

`IHeadHunterAuthorization` is asked for a bearer token before every request and answers whatever
`Sources:HeadHunter:AccessToken` holds (`ConfiguredHeadHunterAuthorization`). Empty is still a valid configuration - it
is what a build without a registered application has - but it no longer polls anything: the api closed the search
endpoints to anonymous callers, so an installation without a token discovers and reads nothing and says so.

The token wanted is an **application** token from `dev.hh.ru`; registration is moderated. The seam is an abstraction
rather than a string because the two tokens the platform can issue are acquired differently: an application token is a
client-credentials call refreshed on a timer, a user token is an authorization-code flow bound to one person. Either can
be added by replacing that one registration; nothing else in the source knows.

A refusal is reported as its own outcome (`HeadHunterFetch.Forbidden`) and logged with the hint, so «the api is asking
for a token» never reads as a company that disappeared.

# Options

`Sources:HeadHunter` - see `HeadHunterOptions`. Three groups worth knowing apart: the paging shape (`PageSize`,
`MaxPages`, `MaxPagedItems`, `MaxDateWindows`), the matching thresholds (`MinMatchScore`, `DecisiveScoreGap`,
`MaxEmployerCandidates`, `OnlyEmployersWithVacancies`) and the pacing (`PauseBetweenRequestsMsec`, the retry steps and
the throttle penalty).

# Models

## HeadHunterFetch

`Ok` / `Missing` / `Failure` / `Refused`. `Missing` covers HTTP 404 **and** the HTTP 400 `bad_argument` the vacancy
search answers for an employer id it does not know - without that, a deleted employer would be polled forever. Anything
else is a failure, so a throttled api can never close a whole board. `Refused` is 401/403: a token question, not a
missing employer, and never retried.

## HeadHunterErrorDto

The refusal body (`{ "description": ..., "errors": [ { "type": "bad_argument", "value": "employer_id" } ] }`). Read
rather than logged as text, because the status code alone does not say what happened: HTTP 400 is a missing board
(`NamesUnknownEmployer`) or a blacklisted caller (`NamesBadUserAgent`), and the two have nothing in common but the code.

## HeadHunterVacancyQuery

One page of the vacancy search: employer, page, page size, sort order and the `date_to` of the current window.

## EmployerDtos / VacancyDtos

`EmployerSearchDto` / `EmployerItemDto` / `EmployerDetailDto`, `VacancySearchDto` / `VacancyItemDto` /
`VacancyDetailDto`. Every field is nullable: this is an unversioned public contract, and a field that disappears must
cost one field, not the board. Only the vacancy `id` is indispensable - items without one are dropped and counted.

## HeadHunterEmployerMatch / HeadHunterUrlParts

One ranked employer of a search result; what a url addressed (an employer, or a vacancy whose employer is not in the
url).

# Infrastructure

## HeadHunterApiClient

Four calls - employer search, employer, vacancy search, vacancy - and the pacing that makes them safe.

The platform documents no rate limit for anonymous traffic, so there is no ceiling to configure and the client is built
around the only signal there is: how the api answers. Every request of the process goes through one gate that keeps a
minimum gap (`PauseBetweenRequestsMsec`), every throttled or failed answer widens that gap by
`ThrottlePenaltyStepSeconds` up to `MaxThrottlePenaltySeconds`, and `ThrottleRecoveryAfterRequests` successes in a row
give one step back. Both directions are logged, so the log shows how hard the api is pushing back. Retries are linear
steps of `RetryDelaySeconds`, honouring `Retry-After` when it asks for more.

429, 408 and 5xx are transient. **403 is not**: it is the api's verdict on the caller, and every retry of it is one more
request against whatever limit produced it. It is reported as `Refused` instead - and so is the HTTP 400 of a blacklisted
user agent, for the same reason: nothing about the request can fix it, only the configuration.

## HeadHunterUserAgent

The agent the client sends. The api blacklists the placeholder contacts of its own examples (`example.com` and friends)
and answers HTTP 400 `bad_user_agent:blacklisted` to them, so a configured agent is checked instead of trusted: an empty
or placeholder one is replaced by `Default` and logged as a warning at registration, because the alternative is every
request of the process failing on a header.

The client is a singleton - the pacing state is the point of it, and a per-request client would have none. The bearer
token comes from `IHeadHunterAuthorization` per request, which is why this is the one source using
`LoggingHttpClient.SendAsync` rather than `GetAsync`.

## HeadHunterCompanyName

Company name to something two names can be compared as: lowercased, unaccented, `ё` folded to `е`, punctuation
collapsed, and legal forms and empty words (`ооо`, `llc`, `group`, `tech`, ...) dropped as whole tokens - so a company
actually called `Group` keeps its name. `Compact` glues the tokens together, which is what makes `head hunter` and
`headhunter` one name and `Яндекс.Такси` the same as `Яндекс Такси`.

Deliberately not a slug guesser: nothing here has to address a board, so there are no `-inc` / `hq` / `get` variants to
try. Transliteration is not attempted either - the catalog search already matches `Yandex` to `Яндекс`, and this only
has to rank what it answered.

## HeadHunterEmployerMatcher

The scoring, and the reason this source needs one at all. `Rank` orders the search results by score, then by open
vacancies (the bigger record is the parent of a group far more often than not), then by name.

Score bands: the same name after normalization is 100; the query being the start of the employer's name is 85 (`Ozon` →
`Ozon Fintech` - the brand is what a user types); the other way round is 75; a substring is 60. Otherwise two ratios are
combined - how much of what was asked for is there, and how much of the employer's name is something else - because both
failure modes matter: a name missing half the query is a different company, and a name burying the query among four
other words usually is too.

The thresholds are not here. What is plausible at all and how far ahead the leader has to be are the resolver's
decision, and the matcher stays a pure function.

## HeadHunterBoardResolver

`ResolveByNameAsync` reads one page of the employer search, ranks it, drops everything under `MinMatchScore`, and then
answers in one of three shapes:

- an exact name, or a leader more than `DecisiveScoreGap` ahead of the runner-up, is answered **alone**;
- a close field is answered **whole** (up to `MaxEmployerCandidates`), so the bot asks which company was meant - the
  choice already exists in `WatchService.LookupAsync`, and using it beats guessing between a group and its subsidiary;
- a field where nothing is plausible is answered with **nothing**, because the tail of a fuzzy search would otherwise
  put a random company into a watchlist.

Candidates come from the search items themselves, so a lookup costs one request however many companies it offers.
Resolution is `DirectSlug` only for an exact name (which is what puts it first in the list the bot shows) and `Catalog`
otherwise.

`ResolveByUrlAsync` takes an employer link, and also a link to a single vacancy - the form a job is actually shared in -
for which it spends one request to learn who posted it. `ProbeAsync` is the validation step of a manual `/board_add` and
of a crawl-mined token: `GET /employers/{id}`, `JobCount` from `open_vacancies`, `DisplayName` the employer's own name.
A non-numeric board id is rejected without a request.

## HeadHunterBoardSource

`GET /vacancies?employer_id={board}`, paged. Two layers, because the search has a ceiling and not just a cap:

- pages are read until the last page the search reports, or a short page;
- `MaxPagedItems` (~2000) is where the api starts refusing the request rather than answering an empty page, so an
  employer with more open vacancies is continued in a **publication-date window**: `date_to` is set to the oldest
  vacancy already seen and paging starts over. This is why `OrderBy` has to be a publication-time order.

`date_to` is inclusive, so every window re-reads its boundary and ids are deduplicated across windows; a window that
brings nothing new ends the traversal. Only `MaxPages` and `MaxDateWindows` produce an incomplete traversal - and an
incomplete one is still reported with what it read, because the orchestrator is what decides not to commit it.

An unaddressable board id is a failure, never a missing board. Descriptions are fetched per vacancy within
`MaxDescriptionRequests`, exactly as SmartRecruiters does it; vacancies past the budget keep the search snippet, which
is enough for a keyword filter to work on.

## HeadHunterMapper

`VacancyItemDto` to `Vacancy`. `BoardId` is always the employer the traversal asked about. `Location` prefers the
address' city (where the job is) over `area` (where the ad was published) and is marked `(remote)` / `(hybrid)` the way
the other sources mark it. `Url` is `alternate_url` - the human page, never the api one. `published_at` is bumped on
every republication, so it is `UpdatedAt`, and `created_at` is the first publication. `GroupId` stays null: there is no
requisition id, so nothing may be collapsed and every post passes deduplication through. A vacancy without an id is
dropped rather than given one.

## HeadHunterBoardUrlParser

`IBoardUrlParser` for crawl index mining: the employer pages of every regional site, as an exact host and as a whole
domain (`*.hh.ru/employer/*`) because every city subdomain serves them too. The token is the employer id, which is
already the address the registry stores - the probe only confirms the employer exists.

Only employer urls yield a token. A crawled vacancy page names its employer nowhere in the url, so mining one would
cost a request per crawled url; the pipeline stays pure and the employer pages are plentiful enough.

## HeadHunterServiceCollectionExtensions

`AddHeadHunterSource(config)` - options, the named http client with the mandatory user agent, the authorization seam,
the singleton api client, the mapper, the keyed source and resolver, and the url parser.
