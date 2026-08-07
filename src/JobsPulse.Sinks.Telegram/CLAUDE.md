# Routines

## TelegramBotListener

Listening user commands and passing handling to command router.

# Infrastructure

## CommandRouter

Responsible for implementing user scenarios business-logic. Returns HTML-response.

Uses pending selection store for storing short dialogue states.

Manages watchlist entries (resolving by name/url, adding/removing, enabling/disabling etc.) on user request.

- /watch CompanyName → search → board candidates list → «1» → added
- /watch &lt;url&gt; → resolve career page
- /list → list watched entries
- /remove CompanyName → unwatch company
- /help

## PendingSelectionStore

Store dialogue states «/watch CompanyName → 1». Stored in memory because dialogue session lasts only a few seconds - no need to restart.

