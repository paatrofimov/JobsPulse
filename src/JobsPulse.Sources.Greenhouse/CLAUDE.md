# Infrastructure

## GreenhouseBoardResolver

Resolves company board via name of career page url parsing.

## GreenhouseBoardSource

Greenhouse vacancy source implementation.

## GreenhouseBoardClient

Thin client over Job Board API.

- no server filtering
- no pagination - board is requested as full
- no rate limit - rate is limited by orchestrator

# SlugGuesser

Generates candidates for board_token from company name.
Most company names are in lowercase.
