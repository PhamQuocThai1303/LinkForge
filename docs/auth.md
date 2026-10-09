# Authentication MVP

## Objective

Visitors can register with email and password or sign in with Google, then use the existing shortener with an account session. Anonymous shortening remains available. Authenticated creations record `urls.user_id`; existing links and management tokens keep their current behavior.

## Contract

- `GET /api/v1/auth/csrf` returns a request token for cookie-backed state-changing auth requests.
- `POST /api/v1/auth/signup` accepts name, email, password; creates an account and signs in.
- `POST /api/v1/auth/login` accepts email, password; signs in without revealing which field was wrong.
- `POST /api/v1/auth/logout` clears the session. These three POSTs require `X-CSRF-TOKEN`.
- `GET /api/v1/auth/me` returns the signed-in user, or 401.
- `GET /api/v1/auth/google` starts Google OAuth, with `/signin-google` as the provider callback; the local completion route creates or finds a Google account and issues the same session cookie.
- Google sign-in is enabled only when client ID and secret are configured. Email collisions with a password account are rejected rather than silently linked; an existing Google subject must continue to report the same verified email. Password hashes and Google subject IDs are never returned.
- Cookies are HttpOnly, SameSite=Lax, and secure on HTTPS. OAuth state/correlation is handled by ASP.NET Core's Google middleware.
- Signup, login, and Google challenge allow at most 20 requests per minute per source IP in each API instance.

## Implementation

- PostgreSQL: nullable `password_hash` and `google_subject` on users; keep `api_key_hash` nullable until API-key functionality exists. Unique email and Google subject constraints.
- ASP.NET Core cookie authentication and `PasswordHasher<User>`. Persistent Data Protection key volume in Compose so sessions survive app container restarts.
- Existing static `/app/` client gains login/signup pages and account state. No password or session token is stored in browser storage.
- The authenticated create route passes user ID to `UrlService`; anonymous requests pass null.

## Commands and project structure

- Build: `dotnet build LinkForge.slnx --no-restore --verbosity quiet`
- Test: `dotnet test LinkForge.slnx --no-restore --verbosity quiet`
- Runtime: `docker compose up --build -d`
- API code: `src/UrlShortener.Api`; schema: `src/UrlShortener.Infrastructure/Persistence`; static UI: `src/UrlShortener.Api/wwwroot/app`; tests: `tests/UrlShortener.IntegrationTests`.

## Code style and verification

Use existing C# minimal API naming and record DTO conventions, plus plain semantic HTML/CSS/JS. For example, `Results.Problem(statusCode: 409, title: "Email already registered.")`. Integration tests cover signup, duplicate email, password login failure/success, session and CSRF protection, logout, and authenticated link ownership. Browser QA covers desktop and mobile forms and account state. Google live consent requires external credentials. Local tests use a fake Google backchannel to cover challenge, verified profile, account creation, and collision behavior.

## Boundaries

- Always: validate account input, avoid secret logging, use server-side password hashing, preserve anonymous create/redirect, keep short-link management tokens independent.
- Ask first: changing external identity provider or account-link policy after release.
- Never: commit OAuth secrets or return password hashes.

## Success criteria

Signup and login work from `/app/`, signed-in state survives reload, logout clears the session, Google sign-in redirects to Google when configured, and authenticated new links have the correct `user_id`. All existing URL tests pass.
