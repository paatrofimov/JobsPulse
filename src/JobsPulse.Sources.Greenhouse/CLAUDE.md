# Infrastructure

## GreenhouseBoardResolver

Resolves company board via name of career page url parsing.

## GreenhouseBoardSource

Greenhouse vacancy source implementation.

## GreenhouseBoardUrlParser

`IBoardUrlParser` for crawl index mining: url patterns of the three Greenhouse hosts and slug extraction reusing
`SlugGuesser.ExtractFromUrl`. Reserved path segments (`embed`, `v1`, `api`, ...) and anything that does not look
like a board token are rejected, so the discovery pipeline does not validate garbage.

## GreenhouseBoardClient

Thin client over Job Board API.

- no server filtering
- no pagination - board is requested as full
- no rate limit - rate is limited by orchestrator

# SlugGuesser

Generates candidates for board_token from company name.
Most company names are in lowercase.
