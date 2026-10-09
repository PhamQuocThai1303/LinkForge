# Account links and public codes

## Objective and acceptance

Signed-in users can see their own shortened URLs, click totals, and change a link's public code. User IDs are UUIDs in PostgreSQL, the session, and auth JSON. Existing account-to-link relationships survive the migration. Anonymous links remain outside account lists.

- Email/password users get an initials avatar; Google users use a validated Google profile image when available and otherwise get initials.
- `GET /api/v1/urls/mine?page=1&pageSize=20` requires a session and returns only the caller's links, newest first, with `shortCode`, `shortUrl`, `originalUrl`, `clickCount`, and `createdAt`. Page size is at most 50.
- `PATCH /api/v1/urls/{shortCode}` with `{ "shortCode": "new-alias" }` requires a session, CSRF header, and ownership. Alias is 3–16 ASCII letters, digits, hyphens, or underscores, with a letter or digit at both ends. Seven-character alphanumeric aliases are reserved for generated codes. Reserved application routes and all current or retired codes cannot be claimed. Returns 404 for links not owned, 409 for unavailable aliases, and validation error for invalid aliases.
- A successful edit changes the public URL. The previous code stops resolving and remains reserved to prevent another link taking over old traffic. Existing management token can still delete the link by its new code.
- Every successful 302 redirect increments the persisted click count atomically. The count is total redirects, not unique visitors.

## Interface and implementation

Keep numeric URL IDs for Base62 generation. Only `users.id` and `urls.user_id` become UUIDs. Add `urls.click_count` and a `reserved_short_codes` table whose primary key is the code. Migrate current codes into the reservation table. Generate new user UUIDs in the application and backfill UUIDs for existing users while remapping URL ownership in one migration transaction. Existing sessions with numeric user claims expire and require a fresh login.

Account list and code edit are separate endpoints under the existing `/api/v1/urls` API. Editing uses the existing Redis tombstone to invalidate the old code. The dashboard is a static `/app/links.html` page and shares the header avatar with `/app/`. Click accounting adds a PostgreSQL write to each redirect; this is the accepted MVP latency tradeoff and should be revisited before high-volume scaling.

## Commands and structure

- Build: `dotnet build LinkForge.slnx --no-restore --verbosity quiet`
- Test: `dotnet test LinkForge.slnx --no-restore --verbosity quiet`
- Runtime: `docker compose up --build -d`
- Domain/schema: `src/UrlShortener.Domain`, `src/UrlShortener.Infrastructure/Persistence`; API: `src/UrlShortener.Api`; UI: `src/UrlShortener.Api/wwwroot/app`; integration tests: `tests/UrlShortener.IntegrationTests`.

## Style, verification, boundaries

Follow existing minimal API C# records and plain semantic HTML/CSS/JS. Test migration on a database with preexisting users and owned URLs, owner isolation, alias validation/collision, old-code behavior, click increments, and static UI assets. Run browser QA on desktop and mobile.

Always keep public redirect and anonymous creation working. Never expose password hashes, management token hashes, or another user's links. Do not store secrets in browser storage. Changes to user IDs and existing sessions are intentional compatibility changes.
